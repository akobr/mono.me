namespace _42.Platform.UI.Admin.AutomationTests.Support;

// The scripts the helpers of PlatformTest add to the browser context (Context.AddInitScriptAsync runs them in every
// page before the app's own scripts) or evaluate in the page. Each init script is idempotent, so it may also run in a
// page that loaded before it was added.
internal static class PageScripts
{
    // Records what the page copies into window.__copied (texts, oldest first) instead of the system clipboard, which a
    // test cannot read on every engine and provider: navigator.clipboard.writeText and write (the text/plain item of a
    // ClipboardItem), and document.execCommand('copy') of the selection (the legacy path of VueUse's useClipboard, which
    // selects a hidden textarea).
    public const string ClipboardRecorder = """
        (() => {
          if (window.__copied) return;
          const copied = [];
          Object.defineProperty(window, '__copied', { value: copied });
          const record = (text) => copied.push(String(text ?? ''));
          const clipboard = navigator.clipboard;
          if (clipboard) {
            Object.defineProperty(clipboard, 'writeText', { configurable: true, value: async (text) => { record(text); } });
            Object.defineProperty(clipboard, 'write', {
              configurable: true,
              value: async (items) => {
                for (const item of items ?? []) {
                  if (item.types.includes('text/plain')) record(await (await item.getType('text/plain')).text());
                }
              },
            });
          }
          const selection = (field) => {
            try {
              return field && typeof field.value === 'string' && field.selectionEnd > field.selectionStart
                ? field.value.slice(field.selectionStart, field.selectionEnd)
                : null;
            } catch {
              return null;
            }
          };
          const execCommand = Document.prototype.execCommand;
          Document.prototype.execCommand = function (command, ...rest) {
            if (String(command).toLowerCase() !== 'copy') return execCommand.call(this, command, ...rest);
            const fields = [document.activeElement, ...document.querySelectorAll('textarea, input')];
            const text = fields.map(selection).find((value) => value !== null);
            record(text ?? document.getSelection()?.toString() ?? '');
            return true;
          };
        })();
        """;

    // Records every fetch of the page into window.__requests: { method, url, body } with the body as text.
    public const string RequestRecorder = """
        (() => {
          if (window.__requests) return;
          const requests = [];
          Object.defineProperty(window, '__requests', { value: requests });
          const text = (body) => {
            if (body === undefined || body === null) return null;
            if (typeof body === 'string') return body;
            if (body instanceof URLSearchParams) return body.toString();
            return `[${Object.prototype.toString.call(body).slice(8, -1)}]`;
          };
          const fetch = window.fetch;
          window.fetch = async function (input, init) {
            const request = input instanceof Request ? input : null;
            const entry = {
              method: String(init?.method ?? request?.method ?? 'GET').toUpperCase(),
              url: new URL(request ? request.url : String(input), location.href).href,
              body: text(init?.body),
            };
            if (entry.body === null && request?.body) {
              try {
                entry.body = await request.clone().text();
              } catch {
                entry.body = '[ReadableStream]';
              }
            }
            requests.push(entry);
            return fetch.call(window, input, init);
          };
        })();
        """;
}
