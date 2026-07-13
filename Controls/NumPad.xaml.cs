using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NumPadDemo.Controls
{
    public partial class NumPad : UserControl
    {
        private const string DisplayTag = "NumPadDisplay";

        private static readonly List<NumPad> Instances = new();
        private static TextBox? _activeTextBox;

        private readonly bool _immediateApply;

        static NumPad()
        {
            EventManager.RegisterClassHandler(typeof(TextBox), GotFocusEvent, new RoutedEventHandler(OnAnyTextBoxGotFocus));
        }

        public NumPad() : this(true)
        {
        }

        public NumPad(bool immediateApply)
        {
            _immediateApply = immediateApply;
            InitializeComponent();

            Instances.Add(this);
            Unloaded += (_, _) => Instances.Remove(this);
        }

        public bool ImmediateApply => _immediateApply;

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
            }
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
            if (_activeTextBox != null)
            {
                _activeTextBox.Text = DisplayTextBox.Text;
                _activeTextBox.CaretIndex = _activeTextBox.Text.Length;
            }

            Keyboard.ClearFocus();
        }
    }
}
