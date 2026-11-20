using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using BEngine.Plugin.VisualStudio.Protocol;
using Microsoft.VisualStudio.PlatformUI;

namespace BEngine.Plugin.VisualStudio
{
    internal sealed class TargetPicker : DialogWindow
    {
        private readonly ListBox _targets;
        private TargetPicker(IReadOnlyList<DebugTarget> targets)
        {
            Title = "Attach to BEngine";
            Width = 580;
            Height = 300;
            MinWidth = 380;
            MinHeight = 240;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new DockPanel { Margin = new Thickness(16) };
            _targets = new ListBox { ItemsSource = targets, SelectedIndex = 0 };
            var label = new TextBlock { Text = "Choose the BEngine Editor to debug", Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(label, Dock.Top);
            panel.Children.Add(label);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var attach = new Button { Content = "Attach", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
            attach.Click += (_, __) => { if (_targets.SelectedItem is not null) DialogResult = true; };
            buttons.Children.Add(attach);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            panel.Children.Add(buttons);
            _targets.MouseDoubleClick += (_, __) => { if (_targets.SelectedItem is not null) DialogResult = true; };
            panel.Children.Add(_targets);
            Content = panel;
        }

        internal static DebugTarget? Select(IReadOnlyList<DebugTarget> targets)
        {
            var dialog = new TargetPicker(targets);
            return dialog.ShowModal() == true ? dialog._targets.SelectedItem as DebugTarget : null;
        }
    }

    internal sealed class SyncProgressDialog : DialogWindow, IDisposable
    {
        internal event Action? Canceled;
        internal SyncProgressDialog()
        {
            Title = "BEngine";
            Width = 420;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = "Synchronizing C# projects…", Margin = new Thickness(0, 0, 0, 12) });
            panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 0, 0, 16) });
            var cancel = new Button { Content = "Cancel", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right };
            cancel.Click += (_, __) => Canceled?.Invoke();
            panel.Children.Add(cancel);
            Content = panel;
            Closing += (_, __) => Canceled?.Invoke();
        }
        public void Dispose() { }
    }
}
