using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class ModernMessageBox : Window
{
    public bool Result { get; private set; }

    private ModernMessageBox(string message, string title, MessageBoxType type, bool showCancel)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        SetIcon(type);
        BuildButtons(type, showCancel);
        Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null;

        var scale = UiScaleService.CurrentPercent / 100d;
        var availableWidth = Owner?.ActualWidth > 0
            ? Math.Min(Owner.ActualWidth, SystemParameters.WorkArea.Width)
            : SystemParameters.WorkArea.Width;
        var availableHeight = Owner?.ActualHeight > 0
            ? Math.Min(Owner.ActualHeight, SystemParameters.WorkArea.Height)
            : SystemParameters.WorkArea.Height;

        Width = Math.Min(520 * scale, availableWidth);
        Height = availableHeight;
        MaxHeight = availableHeight;
        Card.MaxHeight = Math.Max(260, availableHeight / scale - 80);
    }

    private void SetIcon(MessageBoxType type)
    {
        switch (type)
        {
            case MessageBoxType.Info:
                IconCircle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#812B8C")!);
                IconText.Text = "i";
                break;
            case MessageBoxType.Success:
                IconCircle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9731A")!);
                IconText.Text = "\u2713";
                break;
            case MessageBoxType.Warning:
                IconCircle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9731A")!);
                IconText.Text = "!";
                break;
            case MessageBoxType.Error:
                IconCircle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BF3939")!);
                IconText.Text = "\u2717";
                break;
            case MessageBoxType.Question:
                IconCircle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#812B8C")!);
                IconText.Text = "?";
                break;
        }
    }

    private void BuildButtons(MessageBoxType type, bool showCancel)
    {
        if (showCancel || type == MessageBoxType.Question)
        {
            var noBtn = new Button { Content = "Nein", Style = (Style)FindResource("SecondaryBtn"), Margin = new Thickness(0, 0, 12, 0) };
            noBtn.Click += (_, _) => { Result = false; Close(); };
            ButtonPanel.Children.Add(noBtn);

            var yesStyle = type == MessageBoxType.Question ? "PrimaryBtn" : "DangerBtn";
            var yesBtn = new Button { Content = "Ja", Style = (Style)FindResource(yesStyle) };
            yesBtn.Click += (_, _) => { Result = true; Close(); };
            ButtonPanel.Children.Add(yesBtn);
        }
        else
        {
            var okBtn = new Button { Content = "OK", Style = (Style)FindResource("PrimaryBtn"), MinWidth = 120 };
            okBtn.Click += (_, _) => { Result = true; Close(); };
            ButtonPanel.Children.Add(okBtn);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Result = false; Close(); }
        if (e.Key == Key.Enter) { Result = true; Close(); }
    }

    // --- Static API ---

    public static void Show(string message, string title = "Information")
    {
        try
        {
            new ModernMessageBox(message, title, MessageBoxType.Info, false).ShowDialog();
        }
        catch
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public static void ShowError(string message, string title = "Fehler")
    {
        try
        {
            new ModernMessageBox(message, title, MessageBoxType.Error, false).ShowDialog();
        }
        catch
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void ShowSuccess(string message, string title = "Erfolg")
    {
        try
        {
            new ModernMessageBox(message, title, MessageBoxType.Success, false).ShowDialog();
        }
        catch
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public static bool ShowConfirm(string message, string title = "Bestätigen")
    {
        try
        {
            var dlg = new ModernMessageBox(message, title, MessageBoxType.Question, true);
            dlg.ShowDialog();
            return dlg.Result;
        }
        catch
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }
    }

    public enum MessageBoxType { Info, Success, Warning, Error, Question }
}
