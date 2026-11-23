// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PdfExtractor.Core;

namespace PdfExtractor.App;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<string> _files = [];
    private CancellationTokenSource? _cancellation;
    private string? _lastDirectory;
    private bool _closeWhenFinished;

    public MainWindow()
    {
        InitializeComponent();
        FileList.ItemsSource = _files;
        AddHandler(DragDrop.DragEnterEvent, DragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, DragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, Drop, RoutingStrategies.Bubble, handledEventsToo: true);
        Closing += OnClosing;
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        if (_cancellation is not null) return;
        var rejected = 0;
        foreach (var path in paths)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath) || !Path.GetExtension(fullPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                { rejected++; continue; }
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!_files.Any(existing => existing.Equals(fullPath, comparison))) _files.Add(fullPath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            { rejected++; }
        }
        FileCount.Text = _files.Count == 0 ? "Файлы не выбраны. Можно добавить несколько PDF." : $"Выбрано PDF: {_files.Count}";
        if (rejected > 0) Status.Text = $"Пропущено файлов: {rejected}. Добавляйте только существующие PDF.";
        UpdateOutputPreview();
    }

    private async void ChoosePdf(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выбрать PDF", AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"], MimeTypes = ["application/pdf"] }]
        });
        AddFiles(files.Select(file => file.TryGetLocalPath()).OfType<string>());
    }

    private async void ChooseFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Папка сохранения" });
        if (folders.Count > 0) OutputInput.Text = folders[0].TryGetLocalPath();
    }

    private void RemoveFile(object? sender, RoutedEventArgs e)
    {
        foreach (var path in FileList.SelectedItems!.Cast<string>().ToArray()) _files.Remove(path);
        AddFiles([]);
    }

    private void ClearFiles(object? sender, RoutedEventArgs e) { _files.Clear(); AddFiles([]); }

    private void DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = _cancellation is null && e.DataTransfer.TryGetFiles()?.Any(file =>
            file.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (_cancellation is null) AddFiles(e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>() ?? []);
        e.Handled = true;
    }

    private void OutputChanged(object? sender, TextChangedEventArgs e) => UpdateOutputPreview();

    private void UpdateOutputPreview()
    {
        if (OutputPreview is null || OutputInput is null) return;
        OutputPreview.Text = !string.IsNullOrWhiteSpace(OutputInput.Text)
            ? "Все изображения будут сохранены в указанную папку. Существующие файлы сохранятся."
            : _files.Count == 1 ? new ExtractionOptions().GetOutputDirectory(_files[0])
            : "Пустое поле — отдельная папка для каждого PDF.";
    }

    private async void StartExtraction(object? sender, RoutedEventArgs e)
    {
        if (_cancellation is not null) return;
        if (_files.Count == 0) { Status.Text = "Сначала выберите PDF или перетащите его в окно."; return; }
        ExtractionOptions options;
        ExtractionService service;
        try
        {
            if (MinWidthInput.Value is null || MinHeightInput.Value is null
                || MinWidthInput.Value != decimal.Truncate(MinWidthInput.Value.Value)
                || MinHeightInput.Value != decimal.Truncate(MinHeightInput.Value.Value))
                throw new ArgumentException("Введите целые минимальные размеры в пикселях.");
            options = new((int)MinWidthInput.Value.Value, (int)MinHeightInput.Value.Value,
                (OutputFormat)FormatInput.SelectedIndex, TemplateInput.Text ?? "", OutputInput.Text);
            options.Validate();
            service = new ExtractionService(new PdfImages());
        }
        catch (Exception exception) { Status.Text = exception.Message; return; }

        var files = _files.ToArray();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        SetBusy(true);
        Log.Text = "";
        _lastDirectory = null;
        OpenFolderButton.IsVisible = false;
        var saved = 0;
        var skipped = 0;
        var failed = 0;
        try
        {
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                AppendLog($"PDF: {Path.GetFileName(file)}");
                try
                {
                    var progress = new Progress<ExtractionProgress>(value =>
                    {
                        Status.Text = $"{Path.GetFileName(file)}: {value.Message}";
                        Progress.IsIndeterminate = value.Total == 0;
                        if (value.Total > 0) Progress.Value = value.Completed * 100.0 / value.Total;
                    });
                    var result = await Task.Run(() => service.ExtractAsync(file, options, progress, token), token);
                    saved += result.Saved;
                    skipped += result.Skipped;
                    if (result.Saved > 0) _lastDirectory = result.Directory;
                    AppendLog($"Сохранено: {result.Saved}; отсечено: {result.Skipped}." +
                        (result.Saved > 0 ? $" Папка: {result.Directory}" : ""));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { failed++; AppendLog($"Ошибка: {exception.Message}"); }
            }
            Progress.Value = 100;
            Status.Text = $"Готово. Изображений: {saved}; отсечено: {skipped}; ошибок PDF: {failed}.";
        }
        catch (OperationCanceledException)
        {
            Status.Text = "Операция отменена. Уже сохранённые изображения остаются в папке.";
            AppendLog(Status.Text);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            SetBusy(false);
            OpenFolderButton.IsVisible = _lastDirectory is not null;
            if (_closeWhenFinished) Close();
        }
    }

    private void AppendLog(string message)
    {
        var text = Log.Text ?? "";
        if (text.Length > 20000) text = text[^15000..];
        Log.Text = text + message + Environment.NewLine;
        Log.CaretIndex = Log.Text.Length;
    }

    private void SetBusy(bool busy)
    {
        SettingsPanel.IsEnabled = ChoosePdfButton.IsEnabled = RemoveButton.IsEnabled = ClearButton.IsEnabled = StartButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        Progress.IsIndeterminate = busy;
    }

    private void CancelExtraction(object? sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        Status.Text = "Отмена…";
        _cancellation?.Cancel();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_cancellation is null) return;
        e.Cancel = true;
        _closeWhenFinished = true;
        _cancellation.Cancel();
    }

    private void OpenOutputFolder(object? sender, RoutedEventArgs e)
    {
        if (_lastDirectory is null) return;
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(_lastDirectory) { UseShellExecute = true });
            else
            {
                var info = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                info.ArgumentList.Add(_lastDirectory);
                Process.Start(info);
            }
        }
        catch (Exception exception) { Status.Text = "Не удалось открыть папку: " + exception.Message; }
    }
}
