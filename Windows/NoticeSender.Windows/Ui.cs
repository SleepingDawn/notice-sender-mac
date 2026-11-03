using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using NoticeSender.Core;

namespace NoticeSender.Windows;

internal static class Ui
{
    public static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(24, 43, 65));
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(26, 101, 130));
    public static void InstallStyles(Application app)
    {
        var text = new Style(typeof(TextBlock));
        text.Setters.Add(new Setter(TextBlock.ForegroundProperty, Ink));
        app.Resources.Add(typeof(TextBlock), text);
        var button = new Style(typeof(Button));
        button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(13, 7, 13, 7)));
        button.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 7)));
        button.Setters.Add(new Setter(Control.MinHeightProperty, 34.0));
        app.Resources.Add(typeof(Button), button);
        var box = new Style(typeof(TextBox));
        box.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        box.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(198, 211, 221))));
        app.Resources.Add(typeof(TextBox), box);
        var grid = new Style(typeof(DataGrid));
        grid.Setters.Add(new Setter(DataGrid.RowHeightProperty, 34.0));
        grid.Setters.Add(new Setter(DataGrid.ColumnHeaderHeightProperty, 37.0));
        grid.Setters.Add(new Setter(DataGrid.GridLinesVisibilityProperty, DataGridGridLinesVisibility.Horizontal));
        grid.Setters.Add(new Setter(DataGrid.HorizontalGridLinesBrushProperty, new SolidColorBrush(Color.FromRgb(229, 235, 240))));
        grid.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(211, 222, 230))));
        grid.Setters.Add(new Setter(DataGrid.AlternatingRowBackgroundProperty, new SolidColorBrush(Color.FromRgb(248, 250, 252))));
        app.Resources.Add(typeof(DataGrid), grid);
    }
    public static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
    public static TextBox Box(string value = "", bool multiline = false) => new() { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 78 : 34, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 9) };
    public static Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Content = text };
        if (primary) { b.Background = Accent; b.Foreground = Brushes.White; b.BorderBrush = Accent; }
        b.Click += (_, _) => action();
        return b;
    }
    public static WrapPanel Actions(params UIElement[] elements)
    {
        var p = new WrapPanel { Margin = new Thickness(0, 4, 0, 7) };
        foreach (var e in elements) p.Children.Add(e);
        return p;
    }
    public static StackPanel Stack(params UIElement[] elements)
    {
        var s = new StackPanel(); foreach (var e in elements) s.Children.Add(e); return s;
    }
    public static UIElement Field(string title, FrameworkElement control)
    {
        return Stack(Text(title, 12), control);
    }
    public static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    public static DataGrid Table(IEnumerable? source = null)
    {
        return new DataGrid { AutoGenerateColumns = false, ItemsSource = source, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.CellOrRowHeader, ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader, MinHeight = 150, Margin = new Thickness(0, 3, 0, 12), EnableRowVirtualization = true };
    }
    public static void Col(DataGrid grid, string title, string path, double width = 125, bool readOnly = false)
    {
        grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, ValidatesOnExceptions = true, NotifyOnValidationError = true, ConverterCulture = CultureInfo.InvariantCulture }, Width = width, IsReadOnly = readOnly });
    }
    public static void CheckCol(DataGrid grid, string title, string path, double width = 60)
    {
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = title, Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = width });
    }
    public static bool Commit(DataGrid grid)
    {
        return grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true);
    }
    public static void PasteRectangle(DataGrid grid)
    {
        if (!Clipboard.ContainsText()) return;
        PasteRectangle(grid, Clipboard.GetText());
    }
    public static void PasteRectangle(DataGrid grid, string text)
    {
        var rows = SheetParser.ParseDelimited(text);
        var start = grid.CurrentCell;
        if (start.Column is null || start.Item is null) throw new InvalidOperationException("붙여넣을 첫 번째 셀을 선택하세요.");
        if (!Commit(grid)) throw new InvalidOperationException("편집 중인 셀 값을 먼저 확인하세요.");
        int r0 = grid.Items.IndexOf(start.Item);
        var columns = grid.Columns.Select((column, index) => new { column, order = column.DisplayIndex >= 0 ? column.DisplayIndex : index }).OrderBy(c => c.order).Select(c => c.column).ToList();
        int c0 = columns.IndexOf(start.Column);
        if (r0 < 0 || c0 < 0) throw new InvalidOperationException("현재 표의 붙여넣을 첫 번째 셀을 다시 선택하세요.");
        // Validate every value before applying any part of the rectangle.
        var changes = new List<(object owner, PropertyInfo property, object? value)>();
        for (int r = 0; r < rows.Count && r0 + r < grid.Items.Count; r++)
        for (int c = 0; c < rows[r].Count && c0 + c < columns.Count; c++)
        {
            var column = columns[c0 + c];
            if (column.IsReadOnly || column is not DataGridBoundColumn bound || bound.Binding is not Binding binding) continue;
            object owner = grid.Items[r0 + r];
            var parts = binding.Path.Path.Split('.');
            foreach (string part in parts.Take(parts.Length - 1)) owner = owner.GetType().GetProperty(part)!.GetValue(owner)!;
            var prop = owner.GetType().GetProperty(parts[^1])!;
            Type type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            string value = rows[r][c];
            object? parsed = type == typeof(string) ? value : string.IsNullOrWhiteSpace(value) && Nullable.GetUnderlyingType(prop.PropertyType) != null ? null : type == typeof(bool) ? value is "1" or "true" or "TRUE" or "예" or "✓" : Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            changes.Add((owner, prop, parsed));
        }
        foreach (var change in changes) change.property.SetValue(change.owner, change.value);
        grid.Items.Refresh();
    }
    public static void CommitBindings(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>();
        bool hasError = false;
        void Visit(DependencyObject element)
        {
            if (!visited.Add(element)) return;
            if (element is TextBox box) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (Validation.GetHasError(element)) hasError = true;
            foreach (object child in LogicalTreeHelper.GetChildren(element)) if (child is DependencyObject dependency) Visit(dependency);
        }
        Visit(root);
        if (hasError) throw new InvalidOperationException("입력값이 올바르지 않은 항목이 있습니다. 빨간 테두리의 숫자와 날짜를 확인하세요.");
    }
    public static string? Open(string filter)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = filter }; return d.ShowDialog() == true ? d.FileName : null;
    }
    public static string? Save(string filter, string name)
    {
        var d = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = name }; return d.ShowDialog() == true ? d.FileName : null;
    }
    public static bool Confirm(Window owner, string text, string title = "확인") => MessageBox.Show(owner, text, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
}
