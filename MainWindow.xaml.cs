using System.Windows;
using NumPadDemo.Controls;

namespace NumPadDemo
{
    public partial class MainWindow : Window
    {
        private readonly NumPad _embeddedImmediateNumPad = new(immediateApply: true, isPopup: false);
        private readonly NumPad _embeddedEventNumPad = new(immediateApply: false, isPopup: false);
        private readonly NumPad _popupNumPad = new(immediateApply: true, isPopup: true);

        public MainWindow()
        {
            InitializeComponent();

            _embeddedImmediateNumPad.Height = 320;
            EmbeddedImmediateHost.Content = _embeddedImmediateNumPad;
            _embeddedImmediateNumPad.RegisterTextBox(EmbeddedImmediateTextBox);

            _embeddedEventNumPad.Height = 320;
            EmbeddedEventHost.Content = _embeddedEventNumPad;
            _embeddedEventNumPad.Committed += OnEmbeddedEventNumPadCommitted;
            _embeddedEventNumPad.RegisterTextBox(EmbeddedEventTextBox);

            _popupNumPad.RegisterTextBox(PopupTextBox);
        }

        private void OnEmbeddedEventNumPadCommitted(object? sender, NumPadCommitEventArgs e)
        {
            if (e.Target != null)
            {
                e.Target.Text = e.Value;
            }
        }
    }
}
