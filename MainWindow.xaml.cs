using System.Windows;
using NumPadDemo.Controls;

namespace NumPadDemo
{
    public partial class MainWindow : Window
    {
        private readonly NumPad _popupNumPad = new(immediateApply: true, isPopup: true);

        public MainWindow()
        {
            InitializeComponent();
        }
    }
}
