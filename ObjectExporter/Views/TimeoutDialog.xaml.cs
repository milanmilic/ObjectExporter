using System;
using System.Windows;
using System.Windows.Input;

namespace ObjectExporter.Views
{
    public partial class TimeoutDialog : Window
    {
        public event EventHandler CancelRequested;
        public event EventHandler WaitRequested;

        public TimeoutDialog(string message, string cancelText, string waitText)
        {
            InitializeComponent();

            MessageText.Text = message;
            CancelButton.Content = cancelText;
            WaitButton.Content = waitText;

            this.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    CancelRequested?.Invoke(this, EventArgs.Empty);
                    this.Close();
                }
            };
        }

        public void UpdateProgress(int nodesProcessed)
        {
            ProgressText.Text = $"Nodes processed: {nodesProcessed:N0}";
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
            this.Close();
        }

        private void WaitButton_Click(object sender, RoutedEventArgs e)
        {
            WaitRequested?.Invoke(this, EventArgs.Empty);
            this.Close();
        }
    }
}