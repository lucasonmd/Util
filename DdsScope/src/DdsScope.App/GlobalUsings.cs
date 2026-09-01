// The DevExpress WPF packages turn on UseWindowsForms, which makes the SDK add an implicit
// `using System.Windows.Forms;` to every file in this project. That collides with the WPF
// types of the same name.
//
// Alias directives beat namespace imports for simple names, so resolving it once here keeps
// the rest of the project free of `System.Windows.*` qualifications.

global using Application = System.Windows.Application;
global using Binding = System.Windows.Data.Binding;
global using BindingMode = System.Windows.Data.BindingMode;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MessageBox = System.Windows.MessageBox;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
