namespace NotificationService.Api.Common;

/// <summary>Brings the API reference in line with the portfolio's design tokens: warm neutrals, one slate-blue accent, IBM Plex.</summary>
internal static class ScalarStyle
{
    public const string Css = """
        @import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500&family=IBM+Plex+Sans:wght@400;500;600&display=swap');

        :root {
          --scalar-font: 'IBM Plex Sans', system-ui, sans-serif;
          --scalar-font-code: 'IBM Plex Mono', ui-monospace, monospace;
          --scalar-radius: 6px;
          --scalar-radius-lg: 10px;
          --scalar-radius-xl: 10px;
        }

        .light-mode {
          --scalar-background-1: #FFFFFF;
          --scalar-background-2: #F7F6F3;
          --scalar-background-3: #EFEDE8;
          --scalar-border-color: #E2DFD8;
          --scalar-color-1: #1A1A1A;
          --scalar-color-2: #6B6862;
          --scalar-color-3: #8A867F;
          --scalar-color-accent: #2F5D80;
          --scalar-background-accent: #2F5D8014;
          --scalar-button-1: #1F2A37;
          --scalar-button-1-hover: #2B3A4C;
          --scalar-button-1-color: #FFFFFF;
          --scalar-sidebar-background-1: #F7F6F3;
          --scalar-sidebar-item-active-background: #EFEDE8;
          --scalar-sidebar-color-active: #1A1A1A;
          --scalar-sidebar-search-background: #FFFFFF;
          --scalar-sidebar-border-color: #E2DFD8;
          --scalar-shadow-1: none;
          --scalar-shadow-2: 0 1px 2px rgba(26, 26, 26, 0.06);
        }
        """;
}
