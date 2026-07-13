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
        private const string DisplayTag = "NumPadDisplay";

        // Matches d:DesignWidth/d:DesignHeight in NumPad.xaml, used to keep the popup's
        // auto-computed height proportional to whatever width the caller ends up with.
        private const double DesignWidth = 260;
        private const double DesignHeight = 320;

        private static readonly List<NumPad> Instances = new();
        private static TextBox? _activeTextBox;

        private readonly bool _immediateApply;
        private readonly bool _isPopup;
        private readonly Popup? _popup;

        static NumPad()
        {
            EventManager.RegisterClassHandler(typeof(TextBox), GotFocusEvent, new RoutedEventHandler(OnAnyTextBoxGotFocus));
        }

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
                    StaysOpen = false,
                    AllowsTransparency = true,
                    PopupAnimation = PopupAnimation.Fade
                };
            }

            Instances.Add(this);
            Unloaded += (_, _) => Instances.Remove(this);

            if (_activeTextBox != null)
            {
                DisplayTextBox.Text = _activeTextBox.Text;
                DisplayTextBox.CaretIndex = _activeTextBox.Text.Length;
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

        private static void OnAnyTextBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox || (textBox.Tag is string tag && tag == DisplayTag))
            {
                return;
            }

            _activeTextBox = textBox;

            foreach (var numPad in Instances)
            {
                numPad.DisplayTextBox.Text = textBox.Text;
                numPad.DisplayTextBox.CaretIndex = textBox.Text.Length;

                if (numPad._isPopup)
                {
                    numPad.ShowPopupBelow(textBox);
                }
            }
        }

        private void ShowPopupBelow(TextBox target)
        {
            if (_popup == null)
            {
                return;
            }

            var width = target.ActualWidth > 0 ? target.ActualWidth : DesignWidth;
            var height = width * (DesignHeight / DesignWidth);

            Width = width;
            Height = height;

            _popup.PlacementTarget = target;
            _popup.Width = width;
            _popup.Height = height;

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
                _popup.IsOpen = false;
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
