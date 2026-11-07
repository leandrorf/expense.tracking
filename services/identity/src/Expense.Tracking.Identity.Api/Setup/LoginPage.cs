using System.Net;

namespace Expense.Tracking.Identity.Api.Setup;

/// <summary>Renderiza a tela de login minima hospedada pelo proprio Identity Provider.</summary>
public static class LoginPage
{
    /// <summary>Garante que returnUrl seja um caminho local (evita open redirect).</summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/";

    public static string Render(string returnUrl, string? error = null)
    {
        var safeReturn = WebUtility.HtmlEncode(SafeReturnUrl(returnUrl));
        var errorBlock = error is null
            ? string.Empty
            : $"<p class=\"error\" role=\"alert\">{WebUtility.HtmlEncode(error)}</p>";

        return $$"""
        <!doctype html>
        <html lang="pt-br">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>Entrar — Expense Tracking</title>
          <style>
            :root { color-scheme: light dark; }
            * { box-sizing: border-box; }
            body { font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif; margin: 0;
                   min-height: 100vh; display: grid; place-items: center; background: #0f172a; color: #e2e8f0; }
            .card { width: 100%; max-width: 360px; background: #1e293b; padding: 2rem; border-radius: 14px;
                    box-shadow: 0 10px 40px rgba(0,0,0,.4); }
            h1 { font-size: 1.25rem; margin: 0 0 1.25rem; }
            label { display: block; font-size: .8rem; margin: .75rem 0 .25rem; color: #94a3b8; }
            input { width: 100%; padding: .6rem .7rem; border-radius: 8px; border: 1px solid #334155;
                    background: #0f172a; color: #e2e8f0; font-size: .95rem; }
            button { width: 100%; margin-top: 1.25rem; padding: .7rem; border: 0; border-radius: 8px;
                     background: #6366f1; color: white; font-weight: 600; font-size: .95rem; cursor: pointer; }
            button:hover { background: #4f46e5; }
            .error { background: #7f1d1d; color: #fecaca; padding: .6rem .7rem; border-radius: 8px; font-size: .85rem; }
            .hint { margin-top: 1rem; font-size: .75rem; color: #64748b; text-align: center; }
          </style>
        </head>
        <body>
          <form class="card" method="post" action="/account/login/submit">
            <h1>Expense Tracking</h1>
            {{errorBlock}}
            <input type="hidden" name="returnUrl" value="{{safeReturn}}" />
            <label for="email">E-mail</label>
            <input id="email" name="email" type="email" autocomplete="username" required autofocus />
            <label for="password">Senha</label>
            <input id="password" name="password" type="password" autocomplete="current-password" required />
            <button type="submit">Entrar</button>
            <p class="hint">Demo: demo@expense.local / Password123!</p>
          </form>
        </body>
        </html>
        """;
    }
}
