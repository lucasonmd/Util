using System.Windows;
using System.Windows.Controls;

namespace NumPadDemo.Controls
{
    public partial class NumPad : UserControl
    {
        private static TextBox? _activeTextBox;

        static NumPad()
        {
            EventManager.RegisterClassHandler(typeof(TextBox), GotFocusEvent, new RoutedEventHandler(OnAnyTextBoxGotFocus));
        }

        public NumPad()
        {
            InitializeComponent();
        }

        private static void OnAnyTextBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _activeTextBox = textBox;
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
            if (_activeTextBox == null || _activeTextBox.Text.Contains('.'))
            {
                return;
            }

            Insert(_activeTextBox.Text.Length == 0 ? "0." : ".");
        }

        private void SignButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTextBox == null)
            {
                return;
            }

            var text = _activeTextBox.Text;
            _activeTextBox.Text = text.StartsWith("-") ? text[1..] : "-" + text;
            _activeTextBox.CaretIndex = _activeTextBox.Text.Length;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTextBox == null)
            {
                return;
            }

            _activeTextBox.Text = string.Empty;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTextBox == null)
            {
                return;
            }

            var caret = _activeTextBox.CaretIndex;
            if (caret == 0)
            {
                return;
            }

            _activeTextBox.Text = _activeTextBox.Text.Remove(caret - 1, 1);
            _activeTextBox.CaretIndex = caret - 1;
        }

        private void Insert(string toInsert)
        {
            if (_activeTextBox == null)
            {
                return;
            }

            var text = _activeTextBox.Text;

            if (text == "0" && toInsert != ".")
            {
                _activeTextBox.Text = toInsert;
                _activeTextBox.CaretIndex = _activeTextBox.Text.Length;
                return;
            }

            var caret = _activeTextBox.CaretIndex;
            _activeTextBox.Text = text.Insert(caret, toInsert);
            _activeTextBox.CaretIndex = caret + toInsert.Length;
        }
    }
}
