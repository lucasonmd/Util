using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TRA
{
    public partial class KeypadControl : UserControl
    {
        private enum ActiveField { None, AzMin, AzMax, ElMin, ElMax }
        private ActiveField _activeField = ActiveField.None;
        private string      _inputBuffer = "";
        private bool        _isKorean    = false;

        private static readonly Dictionary<string, (string ko, string en)> Strings = new()
        {
            ["azmin"] = ("방위  최소", "AZ  MIN"),
            ["azmax"] = ("방위  최대", "AZ  MAX"),
            ["elmin"] = ("고각  최소", "EL  MIN"),
            ["elmax"] = ("고각  최대", "EL  MAX"),
            ["enter"] = ("입  력",     "ENTER"),
        };

        private string T(string key) => _isKorean ? Strings[key].ko : Strings[key].en;
        private static string UiFont => "Microsoft Sans Serif";

        // field = "azmin" | "azmax" | "elmin" | "elmax"
        public event Action<string, double>? ValueCommitted;

        public KeypadControl()
        {
            InitializeComponent();
        }

        public void SetFieldValues(double azMin, double azMax, double elMin, double elMax)
        {
            ValAzMin.Text = AzStr(azMin);
            ValAzMax.Text = AzStr(azMax);
            ValElMin.Text = ElStr(elMin);
            ValElMax.Text = ElStr(elMax);
        }

        public void ApplyLanguage(bool isKorean)
        {
            _isKorean = isKorean;
            var uf = new FontFamily(UiFont);
            LblAzMin.Text = T("azmin"); LblAzMin.FontFamily = uf;
            LblAzMax.Text = T("azmax"); LblAzMax.FontFamily = uf;
            LblElMin.Text = T("elmin"); LblElMin.FontFamily = uf;
            LblElMax.Text = T("elmax"); LblElMax.FontFamily = uf;
            BtnEnter.Content    = T("enter");
            BtnEnter.FontFamily = uf;
        }

        public void Reset()
        {
            _activeField       = ActiveField.None;
            _inputBuffer       = "";
            KeypadDisplay.Text = "---";
            UpdateFieldHighlights();
        }

        private void UpdateFieldHighlights()
        {
            var map = new (Button btn, ActiveField f)[]
            {
                (BtnSelAzMin, ActiveField.AzMin),
                (BtnSelAzMax, ActiveField.AzMax),
                (BtnSelElMin, ActiveField.ElMin),
                (BtnSelElMax, ActiveField.ElMax),
            };
            foreach (var (btn, f) in map)
            {
                bool active = _activeField == f;
                btn.Background  = new SolidColorBrush(active ? Color.FromRgb(10, 28, 48) : Colors.Black);
                btn.BorderBrush = new SolidColorBrush(active ? Color.FromRgb(44, 90, 132) : Color.FromRgb(20, 30, 42));
            }
        }

        private void FieldBtn_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not string tag) return;
            _activeField = tag switch
            {
                "AzMin" => ActiveField.AzMin,
                "AzMax" => ActiveField.AzMax,
                "ElMin" => ActiveField.ElMin,
                "ElMax" => ActiveField.ElMax,
                _       => ActiveField.None,
            };
            _inputBuffer = "";
            UpdateFieldHighlights();
            KeypadDisplay.Text = "—";
        }

        private void Kbd_Click(object sender, RoutedEventArgs e)
        {
            if (_activeField == ActiveField.None) return;
            string key = (sender as Button)?.Content?.ToString() ?? "";

            bool isAz      = _activeField is ActiveField.AzMin or ActiveField.AzMax;
            bool allowNeg  = true;
            int  maxDigits = isAz ? 4 : 3;

            if (key == "−")
            {
                if (!allowNeg) return;
                _inputBuffer = _inputBuffer.StartsWith("-") ? _inputBuffer[1..] : "-" + _inputBuffer;
            }
            else if (key == "⌫")
            {
                if (_inputBuffer.Length > 0) _inputBuffer = _inputBuffer[..^1];
            }
            else if (key.Length == 1 && char.IsDigit(key[0]))
            {
                if (_inputBuffer.TrimStart('-').Length < maxDigits) _inputBuffer += key;
            }

            KeypadDisplay.Text = _inputBuffer.Length > 0 ? _inputBuffer : "—";
        }

        private void KbdEnter_Click(object sender, RoutedEventArgs e)
        {
            if (_activeField == ActiveField.None || _inputBuffer.Length == 0) return;
            if (!double.TryParse(_inputBuffer, out double value)) return;

            string field = _activeField switch
            {
                ActiveField.AzMin => "azmin",
                ActiveField.AzMax => "azmax",
                ActiveField.ElMin => "elmin",
                ActiveField.ElMax => "elmax",
                _                 => "unknown",
            };

            ValueCommitted?.Invoke(field, value);
            _inputBuffer       = "";
            KeypadDisplay.Text = "---";
        }

        private static string AzStr(double v) =>
            v >= 0 ? $"+{(int)v:0000}" : $"-{(int)Math.Abs(v):0000}";

        private static string ElStr(double v) =>
            v >= 0 ? $"+{(int)v:00}" : $"-{(int)Math.Abs(v):00}";
    }
}
