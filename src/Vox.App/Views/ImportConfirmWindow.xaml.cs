using System.Windows;
using Vox.Core.Storage;

namespace Vox.App.Views;

/// <summary>
/// Shows the dictionary pairs a transfer file adds or replaces and asks before the import (security audit M5):
/// a file from a chat can turn an ordinary word into someone else's wallet address or link.
/// </summary>
public partial class ImportConfirmWindow : Window
{
    public ImportConfirmWindow(TransferPreview preview)
    {
        InitializeComponent();
        var changed = preview.Pairs.Count(p => p.Was is not null);
        SummaryText.Text = "Vox будет менять эти слова во всех диктовках. Загружайте файл, только если вы сделали его сами " +
            $"или доверяете тому, кто его прислал.\nНовых пар: {preview.Pairs.Count - changed}, заменят ваши: {changed}.";
        PairList.ItemsSource = preview.Pairs.Select(p => p.Was is null
            ? $"{p.Wrong}  →  {p.Correct}"
            : $"{p.Wrong}  →  {p.Correct}   (было: {p.Was})").ToList();
        if (preview.Rejected > 0)
        {
            RejectedText.Text = $"Не возьму пар: {preview.Rejected}. В них перенос строки, невидимые знаки или слишком длинный текст.";
            RejectedText.Visibility = Visibility.Visible;
        }
    }

    private void OnImport(object sender, RoutedEventArgs e) => DialogResult = true;
}
