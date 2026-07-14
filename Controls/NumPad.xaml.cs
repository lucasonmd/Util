using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace NumPadDemo.Controls
{
    public partial class NumPad : UserControl
    {
        // Matches d:DesignWidth/d:DesignHeight in NumPad.xaml, used to keep the popup's
        // auto-computed height proportional to whatever width the caller ends up with.
        private const double DesignWidth = 260;
        private const double DesignHeight = 320;

        private readonly bool _immediateApply;
        private readonly bool _isPopup;
        private readonly Popup? _popup;
        private readonly HashSet<TextBox> _registeredTextBoxes = new();
        private TextBox? _activeTextBox;

        public NumPad() : this(false, false)
        {
        }

        public NumPad(bool immediateApply, bool isPopup = false)
        {
            _immediateApply = immediateApply;
            _isPopup = isPopup;
            InitializeComponent();

            if (_isPopup)
            {
                _popup = new Popup
                {
                    Child = this,
                    Placement = PlacementMode.Bottom,
                    StaysOpen = false
                };

                // StaysOpen=false auto-dismisses this popup on an outside click, but a
                // click that lands somewhere non-focusable (e.g. bare window background)
                // never fires GotFocus/LostFocus on the registered TextBox, so it would
                // stay the FocusManager's logical focused element. Clicking it again would
                // then not raise GotFocus and the popup would never reopen. Clearing it
                // here whenever the popup closes (but only if nothing else already took
                // focus) keeps that TextBox re-clickable.
                //
                // Note: deliberately not hooked off this.Unloaded. WPF's Popup unloads its
                // Child every time the popup closes (not just when it's permanently
                // disposed), so an Unloaded handler here would unsubscribe
                // OnRegisteredTextBoxGotFocus after the very first close and the popup
                // would never reopen for any registered TextBox again.
                _popup.Closed += (_, _) =>
                {
                    if (_activeTextBox is TextBox activeTextBox)
                    {
                        var scope = FocusManager.GetFocusScope(activeTextBox);
                        if (Equals(FocusManager.GetFocusedElement(scope), activeTextBox))
                        {
                            FocusManager.SetFocusedElement(scope, null);
                        }
                    }
                };
            }
        }

        public bool ImmediateApply => _immediateApply;

        public bool IsPopup => _isPopup;

        // Only raised when ImmediateApply is false: instead of NumPad writing the
        // value into the active TextBox itself, the caller decides what to do with it.
        public event EventHandler<NumPadCommitEventArgs>? Committed;

        public string GetTextValue()
        {
            return DisplayTextBox.Text;
        }

        public void SetTextValue(string value)
        {
            SyncBuffer(value, value.Length);
        }

        // Each NumPad only reacts to the TextBoxes registered here, instead of every
        // TextBox in the app - otherwise multiple NumPad instances end up mirroring
        // whichever TextBox anywhere last had focus, stomping on each other's value.
        public void RegisterTextBox(IEnumerable<TextBox> textBoxes)
        {
            foreach (var textBox in textBoxes)
            {
                if (_registeredTextBoxes.Add(textBox))
                {
                    textBox.GotFocus += OnRegisteredTextBoxGotFocus;

                    if (_isPopup)
                    {
                        textBox.LostFocus += OnRegisteredTextBoxLostFocus;
                    }
                }
            }
        }

        public void UnregisterTextBox(IEnumerable<TextBox> textBoxes)
        {
            foreach (var textBox in textBoxes)
            {
                if (_registeredTextBoxes.Remove(textBox))
                {
                    textBox.GotFocus -= OnRegisteredTextBoxGotFocus;
                    textBox.LostFocus -= OnRegisteredTextBoxLostFocus;
                }
            }
        }

        private void OnRegisteredTextBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox)
            {
                return;
            }

            _activeTextBox = textBox;
            DisplayTextBox.Text = textBox.Text;
            DisplayTextBox.CaretIndex = textBox.Text.Length;

            if (_isPopup)
            {
                ShowPopupBelow(textBox);
            }
        }

        private void OnRegisteredTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (_popup == null)
            {
                return;
            }

            // Deferred so that, when focus is moving to another registered TextBox for
            // this same popup, that TextBox's GotFocus (which runs synchronously right
            // after this LostFocus, as part of the same focus change) has already
            // updated _activeTextBox by the time this runs - in which case we leave the
            // popup open and just repositioned, instead of closing then reopening it.
            Dispatcher.BeginInvoke(() =>
            {
                if (!ReferenceEquals(_activeTextBox, sender))
                {
                    // Either a different registered TextBox took over (ShowPopupBelow
                    // already repositioned for it), or the popup's own DisplayTextBox
                    // was clicked - neither should close the popup.
                    return;
                }

                if (Equals(Keyboard.FocusedElement, DisplayTextBox))
                {
                    return;
                }

                _popup.IsOpen = false;
            }, DispatcherPriority.Input);
        }

        private void ShowPopupBelow(TextBox target)
        {
            if (_popup == null)
            {
                return;
            }

            // Never shrink below the design width: a numeric keypad's buttons need a
            // minimum usable size regardless of how narrow the target TextBox is.
            var width = Math.Max(target.ActualWidth, DesignWidth);
            var height = width * (DesignHeight / DesignWidth);

            Width = width;
            Height = height;

            _popup.PlacementTarget = target;
            _popup.Width = width;
            _popup.Height = height;

            if (_popup.IsOpen)
            {
                // WPF doesn't reflow the popup's position just because PlacementTarget
                // changed while it's already open - it keeps rendering at the old spot.
                // Nudging an offset forces it to recompute placement without actually
                // closing/reopening, which would otherwise flicker every time focus
                // moves between two registered TextBoxes.
                var offset = _popup.HorizontalOffset;
                _popup.HorizontalOffset = offset + 1;
                _popup.HorizontalOffset = offset;
                return;
            }

            // GotFocus fires on mouse-down; opening synchronously here means the
            // matching mouse-up lands outside the popup and StaysOpen=false reads
            // that as an outside click, closing it before it's ever seen. Deferring
            // past the current input event avoids that race.
            Dispatcher.BeginInvoke(() => _popup.IsOpen = true, DispatcherPriority.Input);
        }

        private void NumberButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string digit })
            {
                Insert(digit);
            }
        }

        private void DecimalButton_Click(object sender, RoutedEventArgs e)
        {
            if (DisplayTextBox.Text.Contains('.'))
            {
                return;
            }

            Insert(DisplayTextBox.Text.Length == 0 ? "0." : ".");
        }

        private void SignButton_Click(object sender, RoutedEventArgs e)
        {
            var text = DisplayTextBox.Text;
            var newText = text.StartsWith("-") ? text[1..] : "-" + text;
            SyncBuffer(newText, newText.Length);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            SyncBuffer(string.Empty, 0);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            var caret = DisplayTextBox.CaretIndex;
            if (caret == 0)
            {
                return;
            }

            var newText = DisplayTextBox.Text.Remove(caret - 1, 1);
            SyncBuffer(newText, caret - 1);
        }

        private void EnterButton_Click(object sender, RoutedEventArgs e)
        {
            CommitAndReleaseFocus();
        }

        private void Insert(string toInsert)
        {
            var text = DisplayTextBox.Text;

            if (text == "0" && toInsert != ".")
            {
                SyncBuffer(toInsert, toInsert.Length);
                return;
            }

            var caret = DisplayTextBox.CaretIndex;
            var newText = text.Insert(caret, toInsert);
            SyncBuffer(newText, caret + toInsert.Length);
        }

        private void SyncBuffer(string newText, int caretIndex)
        {
            DisplayTextBox.Text = newText;
            DisplayTextBox.CaretIndex = caretIndex;

            if (_immediateApply && _activeTextBox != null)
            {
                _activeTextBox.Text = newText;
                _activeTextBox.CaretIndex = caretIndex;
            }
        }

        private void CommitAndReleaseFocus()
        {
            if (_immediateApply)
            {
                if (_activeTextBox != null)
                {
                    _activeTextBox.Text = DisplayTextBox.Text;
                    _activeTextBox.CaretIndex = _activeTextBox.Text.Length;
                }
            }
            else
            {
                Committed?.Invoke(this, new NumPadCommitEventArgs(_activeTextBox, DisplayTextBox.Text));
            }

            Keyboard.ClearFocus();

            if (_popup != null)
            {
                // Closing triggers the Closed handler above, which clears the TextBox's
                // logical focus using its own (real) scope.
                _popup.IsOpen = false;
            }
            else if (_activeTextBox is TextBox activeTextBox)
            {
                // Keyboard.ClearFocus() alone only clears keyboard focus; the TextBox
                // stays the FocusManager's logical focused element, so clicking it again
                // later wouldn't raise GotFocus again.
                var scope = FocusManager.GetFocusScope(activeTextBox);
                if (Equals(FocusManager.GetFocusedElement(scope), activeTextBox))
                {
                    FocusManager.SetFocusedElement(scope, null);
                }
            }
        }
    }

    public sealed class NumPadCommitEventArgs : EventArgs
    {
        public NumPadCommitEventArgs(TextBox? target, string value)
        {
            Target = target;
            Value = value;
        }

        public TextBox? Target { get; }

        public string Value { get; }
    }
}
