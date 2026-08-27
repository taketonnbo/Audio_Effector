using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioEffector.Services;

namespace AudioEffector.Views
{
    public partial class ShortcutInputBox : UserControl
    {
        public static readonly DependencyProperty ShortcutProperty =
            DependencyProperty.Register("Shortcut", typeof(ShortcutKeyConfig), typeof(ShortcutInputBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnShortcutChanged));

        public ShortcutKeyConfig Shortcut
        {
            get { return (ShortcutKeyConfig)GetValue(ShortcutProperty); }
            set { SetValue(ShortcutProperty, value); }
        }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(ShortcutInputBox), new PropertyMetadata(string.Empty));

        public string Label
        {
            get { return (string)GetValue(LabelProperty); }
            set { SetValue(LabelProperty, value); }
        }

        public ShortcutInputBox()
        {
            InitializeComponent();
        }

        private static void OnShortcutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ShortcutInputBox)d;
            control.UpdateDisplayText();
        }

        private void InputTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            InputTextBox.Focus();
            e.Handled = true;
        }

        private void InputTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            InputTextBox.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 0, 120, 215));
            InputTextBox.Text = "キーを入力してください...";
        }

        private void InputTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            InputTextBox.Background = System.Windows.Media.Brushes.Transparent;
            UpdateDisplayText();
        }

        private void UpdateDisplayText()
        {
            if (Shortcut == null || Shortcut.Key == Key.None)
            {
                InputTextBox.Text = "割り当てなし";
            }
            else
            {
                string mods = "";
                if (Shortcut.Modifiers.HasFlag(ModifierKeys.Control)) mods += "Ctrl + ";
                if (Shortcut.Modifiers.HasFlag(ModifierKeys.Shift)) mods += "Shift + ";
                if (Shortcut.Modifiers.HasFlag(ModifierKeys.Alt)) mods += "Alt + ";
                if (Shortcut.Modifiers.HasFlag(ModifierKeys.Windows)) mods += "Win + ";
                InputTextBox.Text = mods + Shortcut.Key.ToString();
            }
        }

        private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ignore modifiers themselves
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LWin || e.Key == Key.RWin ||
                e.Key == Key.System && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt))
            {
                e.Handled = true;
                return;
            }

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            
            // Allow Delete or Backspace to clear shortcut
            if (key == Key.Delete || key == Key.Back)
            {
                Shortcut = new ShortcutKeyConfig { Key = Key.None, Modifiers = ModifierKeys.None };
                WarningTextBlock.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }

            var modifiers = Keyboard.Modifiers;
            
            try
            {
                // Try register to check if it's already in use
                if (IsShortcutInUse(key, modifiers))
                {
                    WarningTextBlock.Text = "⚠️ 警告: このショートカットは他のアプリ等ですでに利用されている可能性があります。";
                    WarningTextBlock.Visibility = Visibility.Visible;
                }
                else
                {
                    WarningTextBlock.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                // Ignore PInvoke exceptions
                WarningTextBlock.Visibility = Visibility.Collapsed;
            }

            // Always create a new object to trigger ViewModel setter
            var newConfig = new ShortcutKeyConfig { Key = key, Modifiers = modifiers };
            Shortcut = newConfig;
            
            // Force text update in case binding doesn't trigger property changed
            UpdateDisplayText();
            
            e.Handled = true;
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private bool IsShortcutInUse(Key key, ModifierKeys modifiers)
        {
            if (key == Key.None) return false;
            
            uint fsModifiers = 0;
            if (modifiers.HasFlag(ModifierKeys.Alt)) fsModifiers |= 0x0001;
            if (modifiers.HasFlag(ModifierKeys.Control)) fsModifiers |= 0x0002;
            if (modifiers.HasFlag(ModifierKeys.Shift)) fsModifiers |= 0x0004;
            if (modifiers.HasFlag(ModifierKeys.Windows)) fsModifiers |= 0x0008;
            
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            int id = 9999; // Random ID

            // If we can register it, it's NOT in use globally.
            bool success = RegisterHotKey(IntPtr.Zero, id, fsModifiers, vk);
            if (success)
            {
                UnregisterHotKey(IntPtr.Zero, id);
                return false;
            }
            
            return true; // Register failed -> in use
        }
    }
}
