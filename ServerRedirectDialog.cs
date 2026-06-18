using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ServerRedirect;

public sealed class ServerRedirectDialog : GuiDialog
{
    private const int PageSize = 9;

    private readonly Action<string> _onRedirectSelected;
    private readonly Action _onRefreshRequested;
    private ServerRedirectEntry[] _entries = [];
    private int _page;

    public override string ToggleKeyCombinationCode => string.Empty;
    public override bool DisableMouseGrab => true;

    public ServerRedirectDialog(ICoreClientAPI capi, Action<string> onRedirectSelected, Action onRefreshRequested)
        : base(capi)
    {
        _onRedirectSelected = onRedirectSelected;
        _onRefreshRequested = onRefreshRequested;
        Compose();
    }

    public void SetEntries(ServerRedirectEntry[] entries)
    {
        _entries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) && !string.IsNullOrWhiteSpace(entry.Host))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ClampPage();
        Compose();
    }

    public override void OnGuiOpened()
    {
        _onRefreshRequested();
        Compose();
        base.OnGuiOpened();
    }

    private void Compose()
    {
        const double width = 260;
        const double titleHeight = 38;
        const double rowHeight = 42;
        const double rowGap = 8;
        const double footerHeight = 54;
        CairoFont bodyFont = CairoFont.WhiteSmallText();

        ClampPage();
        int pageCount = GetPageCount();
        int startIndex = _page * PageSize;
        int rows = Math.Min(PageSize, Math.Max(0, _entries.Length - startIndex));
        int visibleRows = Math.Max(1, rows);
        double listHeight = _entries.Length == 0 ? rowHeight : visibleRows * (rowHeight + rowGap);
        double pagerHeight = pageCount > 1 ? 36 : 0;
        double height = titleHeight + listHeight + pagerHeight + footerHeight + 32;

        ElementBounds dialogBounds = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, width, height);
        ElementBounds bgBounds = ElementBounds.Fixed(0, 0, width, height);

        GuiComposer composer = capi.Gui.CreateCompo("serverredirectdialog", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(ServerRedirectLang.Get("dialog-title"), OnTitleBarClose)
            .BeginChildElements();

        double y = titleHeight + 12;
        if (_entries.Length == 0)
        {
            composer.AddStaticText(
                ServerRedirectLang.Get("dialog-empty"),
                bodyFont,
                ElementBounds.Fixed(18, y + 8, width - 36, rowHeight));
        }
        else
        {
            for (int i = 0; i < rows; i++)
            {
                ServerRedirectEntry entry = _entries[startIndex + i];
                string name = entry.Name;
                string host = entry.Host;
                composer.AddButton(
                    name,
                    () =>
                    {
                        TryClose();
                        _onRedirectSelected(name);
                        return true;
                    },
                    ElementBounds.Fixed(18, y, width - 36, rowHeight),
                    key: "target-" + i);

                y += rowHeight + rowGap;
            }

            if (pageCount > 1)
            {
                composer.AddSmallButton("<<", OnPreviousPage, ElementBounds.Fixed(18, y, 72, 30), key: "previous-page");
                composer.AddStaticText(
                    ServerRedirectLang.Get("dialog-page", _page + 1, pageCount),
                    bodyFont,
                    ElementBounds.Fixed(104, y + 7, width - 208, 26),
                    key: "page-label");
                composer.AddSmallButton(">>", OnNextPage, ElementBounds.Fixed(width - 90, y, 72, 30), key: "next-page");
                y += pagerHeight;
            }
        }

        composer
            .AddSmallButton(ServerRedirectLang.Get("button-refresh"), OnRefresh, ElementBounds.Fixed(18, height - 48, 120, 32))
            .AddSmallButton(Lang.Get("button-close"), OnClose, ElementBounds.Fixed(width - 138, height - 48, 120, 32))
            .EndChildElements();

        SingleComposer = composer.Compose();
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private bool OnRefresh()
    {
        _onRefreshRequested();
        return true;
    }

    private bool OnPreviousPage()
    {
        if (_page > 0)
        {
            _page--;
            Compose();
        }

        return true;
    }

    private bool OnNextPage()
    {
        if (_page < GetPageCount() - 1)
        {
            _page++;
            Compose();
        }

        return true;
    }

    private bool OnClose()
    {
        TryClose();
        return true;
    }

    private int GetPageCount()
    {
        return Math.Max(1, (int)Math.Ceiling(_entries.Length / (double)PageSize));
    }

    private void ClampPage()
    {
        _page = Math.Clamp(_page, 0, GetPageCount() - 1);
    }
}
