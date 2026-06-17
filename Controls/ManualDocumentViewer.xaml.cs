using System.IO;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Xps.Packaging;
using PdfiumViewer;
using DrawingColor = System.Drawing.Color;
using DrawingRectangleF = System.Drawing.RectangleF;
using Forms = System.Windows.Forms;

namespace TensileNeW.Controls;

public partial class ManualDocumentViewer : UserControl
{
    private const double MinZoom = 0.5;
    private const double MaxZoom = 2.0;

    private FixedDocumentSequence? _documentSequence;
    private PdfViewer? _pdfViewer;
    private IPdfDocument? _pdfDocument;
    private readonly List<PdfMatch> _pdfMatches = [];
    private int _lastPdfMatchIndex = -1;
    private readonly List<PageSearchEntry> _searchIndex = [];
    private int _lastMatchedPageIndex = -1;
    private SearchRequest? _lastSearchRequest;

    private static readonly PropertyInfo? DocumentScrollInfoProperty =
        typeof(DocumentViewer).GetProperty("DocumentScrollInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly PropertyInfo? TextEditorProperty =
        typeof(DocumentViewer).GetProperty("TextEditor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly PropertyInfo? SelectionStartProperty;
    private static readonly PropertyInfo? SelectionEndProperty;
    private static readonly PropertyInfo? TextEditorSelectionProperty;
    private static readonly MethodInfo? SelectionSelectMethod;
    private static readonly MethodInfo? SelectionValidateLayoutMethod;
    private static readonly MethodInfo? SelectionUpdateCaretAndHighlightMethod;
    private static readonly MethodInfo? SelectionRefreshCaretMethod;
    private static readonly PropertyInfo? TextRangeStartProperty;
    private static readonly PropertyInfo? TextRangeEndProperty;
    private static readonly MethodInfo? TextContainerStartPropertyGetter;
    private static readonly MethodInfo? TextContainerEndPropertyGetter;
    private static readonly MethodInfo? CreatePointerAtOffsetMethod;
    private static readonly MethodInfo? CreatePointerMethod;
    private static readonly MethodInfo? GetTextInRunMethod;
    private static readonly MethodInfo? GetTextRunLengthMethod;
    private static readonly MethodInfo? MoveToNextContextPositionMethod;
    private static readonly MethodInfo? MoveByOffsetMethod;
    private static readonly MethodInfo? CompareToMethod;
    private static readonly MethodInfo? GetOffsetToPositionMethod;
    private static readonly MethodInfo? MakeSelectionVisibleMethod;
    private static readonly MethodInfo? TextFindEngineFindMethod;
    private static readonly Type? FindFlagsType;
    private static readonly object? FindFlagsNone;
    private static readonly object? FindFlagsReverse;
    private static readonly FieldInfo? FindToolbarField =
        typeof(DocumentViewer).GetField("_findToolbar", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? InstantiateFindToolBarMethod =
        typeof(DocumentViewer).GetMethod("InstantiateFindToolBar", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? DocumentViewerFindMethod;
    private static readonly PropertyInfo? FindToolbarSearchUpProperty;
    private static readonly PropertyInfo? FindToolbarDocumentLoadedProperty;
    private static readonly FieldInfo? FindToolbarTextBoxField;

    static ManualDocumentViewer()
    {
        Assembly assembly = typeof(DocumentViewer).Assembly;
        Type? textSelectionType = assembly.GetType("System.Windows.Documents.TextSelection");
        Type? textContainerType = assembly.GetType("System.Windows.Documents.ITextContainer");
        Type? textPointerType = assembly.GetType("System.Windows.Documents.ITextPointer");
        Type? documentGridType = assembly.GetType("MS.Internal.Documents.DocumentGrid");
        Type? textFindEngineType = assembly.GetType("System.Windows.Documents.TextFindEngine");
        Type? findToolBarType = assembly.GetType("MS.Internal.Documents.FindToolBar");
        FindFlagsType = assembly.GetType("System.Windows.Documents.FindFlags");
        Type logicalDirectionType = typeof(LogicalDirection);

        SelectionStartProperty = textSelectionType?.GetProperty("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        SelectionEndProperty = textSelectionType?.GetProperty("End", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        TextEditorSelectionProperty = TextEditorProperty?.PropertyType.GetProperty("Selection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        TextRangeStartProperty = typeof(TextRange).GetProperty("Start", BindingFlags.Instance | BindingFlags.Public);
        TextRangeEndProperty = typeof(TextRange).GetProperty("End", BindingFlags.Instance | BindingFlags.Public);
        SelectionSelectMethod = textSelectionType?.GetMethod(
            "Select",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                textPointerType!,
                textPointerType!
            ],
            modifiers: null);
        SelectionValidateLayoutMethod = textSelectionType?.GetMethod(
            "ValidateLayout",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        SelectionUpdateCaretAndHighlightMethod = textSelectionType?.GetMethod(
            "UpdateCaretAndHighlight",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        SelectionRefreshCaretMethod = textSelectionType?.GetMethod(
            "RefreshCaret",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        TextContainerStartPropertyGetter = textContainerType?.GetProperty("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetMethod;
        TextContainerEndPropertyGetter = textContainerType?.GetProperty("End", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetMethod;
        CreatePointerAtOffsetMethod = textContainerType?.GetMethod(
            "CreatePointerAtOffset",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(int), logicalDirectionType],
            modifiers: null);
        CreatePointerMethod = textPointerType?.GetMethod(
            "CreatePointer",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        GetTextInRunMethod = textPointerType?.GetMethod(
            "GetTextInRun",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [logicalDirectionType],
            modifiers: null);
        GetTextRunLengthMethod = textPointerType?.GetMethod(
            "GetTextRunLength",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [logicalDirectionType],
            modifiers: null);
        MoveToNextContextPositionMethod = textPointerType?.GetMethod(
            "MoveToNextContextPosition",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [logicalDirectionType],
            modifiers: null);
        MoveByOffsetMethod = textPointerType?.GetMethod(
            "MoveByOffset",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(int)],
            modifiers: null);
        CompareToMethod = textPointerType?.GetMethod(
            "CompareTo",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [textPointerType!],
            modifiers: null);
        GetOffsetToPositionMethod = textPointerType?.GetMethod(
            "GetOffsetToPosition",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [textPointerType!],
            modifiers: null);
        MakeSelectionVisibleMethod = documentGridType?.GetMethod("MakeSelectionVisible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        TextFindEngineFindMethod = textFindEngineType?.GetMethod(
            "Find",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                textPointerType!,
                textPointerType!,
                typeof(string),
                FindFlagsType!,
                typeof(CultureInfo)
            ],
            modifiers: null);
        if (FindFlagsType is not null)
        {
            FindFlagsNone = Enum.ToObject(FindFlagsType, 0);
            FindFlagsReverse = Enum.Parse(FindFlagsType, "FindInReverse");
        }
        if (findToolBarType is not null)
        {
            DocumentViewerFindMethod = typeof(DocumentViewer).GetMethod(
                "Find",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: [findToolBarType],
                modifiers: null);
            FindToolbarSearchUpProperty = findToolBarType.GetProperty("SearchUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FindToolbarDocumentLoadedProperty = findToolBarType.GetProperty("DocumentLoaded", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FindToolbarTextBoxField = findToolBarType.GetField("FindTextBox", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
    }

    public ManualDocumentViewer()
    {
        InitializeComponent();
    }

    public void SetDocument(XpsDocument document)
    {
        ClearPdfDocument();
        InnerDocumentViewer.Visibility = Visibility.Visible;
        PdfHost.Visibility = Visibility.Collapsed;
        _documentSequence = document.GetFixedDocumentSequence();
        InnerDocumentViewer.Document = _documentSequence;
        RebuildSearchIndex();
        _lastMatchedPageIndex = -1;
    }

    public void SetPdfDocument(string path)
    {
        ClearDocument();
        EnsurePdfViewer();

        _pdfDocument = PdfDocument.Load(path);
        _pdfViewer!.Document = _pdfDocument;
        _pdfViewer.ZoomMode = PdfViewerZoomMode.FitWidth;
        InnerDocumentViewer.Visibility = Visibility.Collapsed;
        PdfHost.Visibility = Visibility.Visible;
        _lastPdfMatchIndex = -1;
        _pdfMatches.Clear();
    }

    public void ClearDocument()
    {
        InnerDocumentViewer.Document = null;
        _documentSequence = null;
        _searchIndex.Clear();
        _lastMatchedPageIndex = -1;
        ClearPdfDocument();
    }

    public void SetZoomFactor(double zoomFactor)
    {
        double clampedZoom = Math.Clamp(zoomFactor, MinZoom, MaxZoom);
        if (_pdfViewer is not null && PdfHost.Visibility == Visibility.Visible)
        {
            _pdfViewer.Renderer.ZoomMode = PdfViewerZoomMode.FitBest;
            _pdfViewer.Renderer.Zoom = clampedZoom;
            return;
        }

        InnerDocumentViewer.Zoom = clampedZoom * 100.0;
    }

    public bool Search(string keyword, bool searchWholeDocument, bool forward, out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        keyword = keyword.Trim();
        _lastSearchRequest = new SearchRequest(keyword, searchWholeDocument, forward);

        if (_pdfDocument is not null && _pdfViewer is not null && PdfHost.Visibility == Visibility.Visible)
        {
            return SearchPdf(keyword, searchWholeDocument, forward, out message);
        }

        if (_documentSequence is null || _searchIndex.Count == 0)
        {
            return false;
        }

        if (TrySelectTextMatch(keyword, searchWholeDocument, forward))
        {
            return true;
        }

        int startPage = searchWholeDocument
            ? GetNextStartIndex(forward)
            : GetCurrentPageIndex();
        int matchPage = FindMatchedPage(keyword, startPage, searchWholeDocument, forward);
        if (matchPage < 0)
        {
            message = "未找到对应内容。";
            return false;
        }

        _lastMatchedPageIndex = matchPage;
        GoToPage(matchPage);
        return true;
    }

    public bool RepeatLastSearch(bool forward, out string? message)
    {
        message = null;
        if (_lastSearchRequest is null)
        {
            message = "请先输入搜索内容。";
            return false;
        }

        return Search(_lastSearchRequest.Keyword, _lastSearchRequest.SearchWholeDocument, forward, out message);
    }

    private void EnsurePdfViewer()
    {
        if (_pdfViewer is not null)
        {
            return;
        }

        _pdfViewer = new PdfViewer
        {
            Dock = Forms.DockStyle.Fill,
            ShowToolbar = false,
            ShowBookmarks = false,
            BackColor = DrawingColor.White
        };
        PdfHost.Child = _pdfViewer;
    }

    private void ClearPdfDocument()
    {
        _pdfMatches.Clear();
        _lastPdfMatchIndex = -1;

        if (_pdfViewer is not null)
        {
            _pdfViewer.Document = null;
            _pdfViewer.Renderer.Markers.Clear();
        }

        _pdfDocument?.Dispose();
        _pdfDocument = null;
    }

    private bool SearchPdf(string keyword, bool searchWholeDocument, bool forward, out string? message)
    {
        message = null;
        if (_pdfDocument is null || _pdfViewer is null)
        {
            return false;
        }

        IReadOnlyList<PdfMatch> matches = GetPdfMatches(keyword, searchWholeDocument);
        if (matches.Count == 0)
        {
            message = "未找到对应内容。";
            return false;
        }

        if (!PdfMatchesEqual(matches, _pdfMatches))
        {
            _pdfMatches.Clear();
            _pdfMatches.AddRange(matches);
            _lastPdfMatchIndex = forward ? -1 : _pdfMatches.Count;
        }

        _lastPdfMatchIndex = forward
            ? (_lastPdfMatchIndex + 1) % _pdfMatches.Count
            : (_lastPdfMatchIndex - 1 + _pdfMatches.Count) % _pdfMatches.Count;

        PdfMatch match = _pdfMatches[_lastPdfMatchIndex];
        DrawingRectangleF matchBounds = GetPdfMatchBounds(match);
        _pdfViewer.Renderer.Page = match.Page;
        _pdfViewer.Renderer.Markers.Clear();
        _pdfViewer.Renderer.Markers.Add(new PdfMarker(
            match.Page,
            matchBounds,
            DrawingColor.FromArgb(96, 255, 208, 0),
            DrawingColor.FromArgb(220, 230, 140, 0),
            1));
        _pdfViewer.Renderer.ScrollIntoView(new PdfRectangle(match.Page, matchBounds));
        _pdfViewer.Renderer.Invalidate();
        _pdfViewer.Focus();
        return true;
    }

    private DrawingRectangleF GetPdfMatchBounds(PdfMatch match)
    {
        if (_pdfDocument is null)
        {
            return DrawingRectangleF.Empty;
        }

        PdfRectangle bounds = _pdfDocument.GetTextBounds(match.TextSpan).FirstOrDefault();
        return bounds.IsValid ? bounds.Bounds : DrawingRectangleF.Empty;
    }

    private IReadOnlyList<PdfMatch> GetPdfMatches(string keyword, bool searchWholeDocument)
    {
        if (_pdfDocument is null || _pdfViewer is null)
        {
            return [];
        }

        int currentPage = Math.Clamp(_pdfViewer.Renderer.Page, 0, Math.Max(0, _pdfDocument.PageCount - 1));
        PdfMatches matches = searchWholeDocument
            ? _pdfDocument.Search(keyword, matchCase: false, wholeWord: false)
            : _pdfDocument.Search(keyword, matchCase: false, wholeWord: false, currentPage);

        return matches.Items
            .OrderBy(match => match.Page)
            .ThenBy(match => match.TextSpan.Offset)
            .ToList();
    }

    private static bool PdfMatchesEqual(IReadOnlyList<PdfMatch> left, IReadOnlyList<PdfMatch> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (left[index].Page != right[index].Page ||
                left[index].TextSpan.Offset != right[index].TextSpan.Offset ||
                left[index].TextSpan.Length != right[index].TextSpan.Length)
            {
                return false;
            }
        }

        return true;
    }

    private void RebuildSearchIndex()
    {
        _searchIndex.Clear();
        if (_documentSequence is null)
        {
            return;
        }

        DocumentPaginator paginator = ((IDocumentPaginatorSource)_documentSequence).DocumentPaginator;
        for (int pageIndex = 0; pageIndex < paginator.PageCount; pageIndex++)
        {
            DocumentPage page = paginator.GetPage(pageIndex);
            string pageText = ExtractText(page.Visual);
            _searchIndex.Add(new PageSearchEntry(pageIndex, pageText));
            if (page is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static string ExtractText(Visual? visual)
    {
        if (visual is null)
        {
            return string.Empty;
        }

        StringWriter writer = new();
        WriteText(visual, writer);
        return writer.ToString();
    }

    private static void WriteText(DependencyObject node, StringWriter writer)
    {
        if (node is Glyphs glyphs && !string.IsNullOrWhiteSpace(glyphs.UnicodeString))
        {
            writer.Write(glyphs.UnicodeString);
            writer.Write(' ');
        }
        else if (node is TextBlock textBlock)
        {
            string text = new TextRange(textBlock.ContentStart, textBlock.ContentEnd).Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                writer.Write(text);
                writer.Write(' ');
            }
        }
        else if (node is FlowDocumentScrollViewer viewer && viewer.Document is not null)
        {
            string text = new TextRange(viewer.Document.ContentStart, viewer.Document.ContentEnd).Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                writer.Write(text);
                writer.Write(' ');
            }
        }

        int childCount = VisualTreeHelper.GetChildrenCount(node);
        for (int index = 0; index < childCount; index++)
        {
            WriteText(VisualTreeHelper.GetChild(node, index), writer);
        }
    }

    private int GetCurrentPageIndex()
    {
        int pageNumber = InnerDocumentViewer.MasterPageNumber;
        return pageNumber > 0 ? pageNumber - 1 : 0;
    }

    private int GetNextStartIndex(bool forward)
    {
        if (forward && _lastMatchedPageIndex >= 0 && _lastMatchedPageIndex + 1 < _searchIndex.Count)
        {
            return _lastMatchedPageIndex + 1;
        }

        if (!forward && _lastMatchedPageIndex > 0)
        {
            return _lastMatchedPageIndex - 1;
        }

        int currentPage = GetCurrentPageIndex();
        if (currentPage >= 0 && currentPage < _searchIndex.Count)
        {
            return currentPage;
        }

        return 0;
    }

    private int FindMatchedPage(string keyword, int startPage, bool wrapSearch, bool forward)
    {
        StringComparison comparison = StringComparison.CurrentCultureIgnoreCase;

        if (forward)
        {
            for (int index = Math.Max(0, startPage); index < _searchIndex.Count; index++)
            {
                if (_searchIndex[index].Text.Contains(keyword, comparison))
                {
                    return _searchIndex[index].PageIndex;
                }
            }
            if (!wrapSearch)
            {
                return -1;
            }

            for (int index = 0; index < Math.Max(0, startPage); index++)
            {
                if (_searchIndex[index].Text.Contains(keyword, comparison))
                {
                    return _searchIndex[index].PageIndex;
                }
            }
        }
        else
        {
            for (int index = Math.Min(startPage, _searchIndex.Count - 1); index >= 0; index--)
            {
                if (_searchIndex[index].Text.Contains(keyword, comparison))
                {
                    return _searchIndex[index].PageIndex;
                }
            }

            if (!wrapSearch)
            {
                return -1;
            }

            for (int index = _searchIndex.Count - 1; index > Math.Min(startPage, _searchIndex.Count - 1); index--)
            {
                if (_searchIndex[index].Text.Contains(keyword, comparison))
                {
                    return _searchIndex[index].PageIndex;
                }
            }
        }

        return -1;
    }

    private bool TrySelectTextMatch(string keyword, bool searchWholeDocument, bool forward)
    {
        if (TrySelectTextMatchWithNativeToolbar(keyword, forward))
        {
            return true;
        }

        object? documentGrid = DocumentScrollInfoProperty?.GetValue(InnerDocumentViewer);
        object? textEditor = TextEditorProperty?.GetValue(InnerDocumentViewer);
        if (documentGrid is null || textEditor is null)
        {
            return false;
        }

        object? selection = TextEditorSelectionProperty?.GetValue(textEditor);
        object? textContainer = documentGrid.GetType().GetProperty("TextContainer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(documentGrid);
        if (selection is null || textContainer is null ||
            SelectionStartProperty is null || SelectionEndProperty is null || SelectionSelectMethod is null ||
            SelectionValidateLayoutMethod is null || SelectionUpdateCaretAndHighlightMethod is null || SelectionRefreshCaretMethod is null ||
            TextRangeStartProperty is null || TextRangeEndProperty is null ||
            TextContainerStartPropertyGetter is null || TextContainerEndPropertyGetter is null ||
            TextFindEngineFindMethod is null || FindFlagsNone is null || FindFlagsReverse is null)
        {
            return false;
        }

        object startPointer = SelectionStartProperty.GetValue(selection)!;
        object endPointer = SelectionEndProperty.GetValue(selection)!;
        object containerStart = TextContainerStartPropertyGetter.Invoke(textContainer, null)!;
        object containerEnd = TextContainerEndPropertyGetter.Invoke(textContainer, null)!;

        object? foundRange = FindNativeRange(keyword, startPointer, endPointer, containerStart, containerEnd, forward, searchWholeDocument);
        if (foundRange is null)
        {
            return false;
        }

        object? rangeStart = TextRangeStartProperty.GetValue(foundRange);
        object? rangeEnd = TextRangeEndProperty.GetValue(foundRange);
        if (rangeStart is null || rangeEnd is null)
        {
            return false;
        }

        SelectionSelectMethod.Invoke(selection, [rangeStart, rangeEnd]);
        InnerDocumentViewer.Focus();
        SelectionValidateLayoutMethod?.Invoke(selection, null);
        SelectionUpdateCaretAndHighlightMethod?.Invoke(selection, null);
        SelectionRefreshCaretMethod?.Invoke(selection, null);
        MakeSelectionVisibleMethod?.Invoke(documentGrid, null);
        return true;
    }

    private bool TrySelectTextMatchWithNativeToolbar(string keyword, bool forward)
    {
        if (FindToolbarField is null ||
            InstantiateFindToolBarMethod is null ||
            DocumentViewerFindMethod is null ||
            FindToolbarSearchUpProperty is null ||
            FindToolbarDocumentLoadedProperty is null ||
            FindToolbarTextBoxField is null)
        {
            return false;
        }

        object? findToolBar = FindToolbarField.GetValue(InnerDocumentViewer);
        if (findToolBar is null)
        {
            InstantiateFindToolBarMethod.Invoke(InnerDocumentViewer, null);
            findToolBar = FindToolbarField.GetValue(InnerDocumentViewer);
        }

        if (findToolBar is null)
        {
            return false;
        }

        if (FindToolbarTextBoxField.GetValue(findToolBar) is not TextBox findTextBox)
        {
            return false;
        }

        FindToolbarDocumentLoadedProperty.SetValue(findToolBar, true);
        FindToolbarSearchUpProperty.SetValue(findToolBar, !forward);
        findTextBox.Text = keyword;

        object? result = DocumentViewerFindMethod.Invoke(InnerDocumentViewer, [findToolBar]);
        InnerDocumentViewer.Focus();
        return result is not null;
    }

    private object? FindNativeRange(
        string keyword,
        object selectionStart,
        object selectionEnd,
        object containerStart,
        object containerEnd,
        bool forward,
        bool wrapSearch)
    {
        object? firstPass = forward
            ? InvokeNativeFind(selectionEnd, containerEnd, keyword, forward)
            : InvokeNativeFind(containerStart, selectionStart, keyword, forward);
        if (firstPass is not null)
        {
            return firstPass;
        }

        if (!wrapSearch)
        {
            return null;
        }

        return InvokeNativeFind(containerStart, containerEnd, keyword, forward);
    }

    private object? InvokeNativeFind(
        object startPointer,
        object endPointer,
        string keyword,
        bool forward)
    {
        object flags = forward ? FindFlagsNone! : FindFlagsReverse!;
        return TextFindEngineFindMethod!.Invoke(null, [startPointer, endPointer, keyword, flags, CultureInfo.CurrentCulture]);
    }

    private void GoToPage(int pageIndex)
    {
        int pageNumber = pageIndex + 1;
        if (!InnerDocumentViewer.CanGoToPage(pageNumber))
        {
            return;
        }

        InnerDocumentViewer.GoToPage(pageNumber);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => InnerDocumentViewer.Focus());
    }

    private sealed record PageSearchEntry(int PageIndex, string Text);
    private sealed record SearchRequest(string Keyword, bool SearchWholeDocument, bool Forward);
}
