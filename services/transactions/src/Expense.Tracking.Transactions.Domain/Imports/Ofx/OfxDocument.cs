using System.Globalization;
using System.Text;
using System.Xml;
using Expense.Tracking.Transactions.Domain.Common;

namespace Expense.Tracking.Transactions.Domain.Imports.Ofx;

/// <summary>Elemento do OFX: agregado (com filhos) ou folha (com valor). Nomes em maiusculas.</summary>
internal sealed class OfxNode(string name)
{
    public string Name { get; } = name;

    public string? Value { get; set; }

    public List<OfxNode> Children { get; } = [];

    public OfxNode? Child(string name) => Children.FirstOrDefault(c => c.Name == name);

    public string? ChildValue(string name) => Child(name)?.Value;

    public IEnumerable<OfxNode> Descendants(string name)
    {
        foreach (var child in Children)
        {
            if (child.Name == name)
            {
                yield return child;
            }

            foreach (var match in child.Descendants(name))
            {
                yield return match;
            }
        }
    }
}

/// <summary>
/// Le os bytes de um OFX 1.x (SGML) ou 2.x (XML) numa arvore de <see cref="OfxNode"/>.
/// Nenhuma declaracao (<c>&lt;!DOCTYPE&gt;</c>, entidade) e resolvida: arquivo com ela e recusado
/// (ADR-0004, decisao 7). As mensagens de erro nunca citam o conteudo do arquivo.
/// </summary>
internal static class OfxDocument
{
    private const int HeaderProbeLength = 1024;

    public static Result<OfxNode> Parse(ReadOnlySpan<byte> content)
    {
        var bytes = content;
        var hasUtf8Bom = bytes.StartsWith(Encoding.UTF8.Preamble);
        if (hasUtf8Bom)
        {
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        }

        // O cabecalho e ASCII nas duas versoes; Latin-1 so para inspecionar, sem perder bytes.
        var probe = Encoding.Latin1.GetString(bytes[..Math.Min(bytes.Length, HeaderProbeLength)]).TrimStart();

        if (probe.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            return ParseXml(bytes);
        }

        if (probe.StartsWith("OFXHEADER", StringComparison.OrdinalIgnoreCase) ||
            probe.StartsWith("<OFX>", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSgml(bytes, hasUtf8Bom);
        }

        return Result.Failure<OfxNode>(ImportErrors.FormatInvalid("O arquivo nao e um OFX."));
    }

    private static Result<OfxNode> ParseXml(ReadOnlySpan<byte> bytes)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };

        var root = new OfxNode(string.Empty);
        var stack = new Stack<OfxNode>();
        stack.Push(root);

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, settings);
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        var node = new OfxNode(reader.LocalName.ToUpperInvariant());
                        stack.Peek().Children.Add(node);
                        if (!reader.IsEmptyElement)
                        {
                            stack.Push(node);
                        }

                        break;
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                        stack.Peek().Value = reader.Value.Trim();
                        break;
                    case XmlNodeType.EndElement:
                        stack.Pop();
                        break;
                }
            }
        }
        catch (XmlException)
        {
            // A mensagem da XmlException pode citar trechos do arquivo: nao e repassada.
            return Result.Failure<OfxNode>(ImportErrors.FormatInvalid(
                "O OFX esta malformado, incompleto ou tem declaracoes nao permitidas (DOCTYPE, entidades)."));
        }

        return root;
    }

    private static Result<OfxNode> ParseSgml(ReadOnlySpan<byte> bytes, bool hasUtf8Bom)
    {
        var headerEnd = bytes.IndexOf((byte)'<');
        if (headerEnd < 0)
        {
            return Result.Failure<OfxNode>(ImportErrors.FormatInvalid("O OFX nao tem conteudo."));
        }

        var encoding = hasUtf8Bom ? Encoding.UTF8 : SgmlEncoding(Encoding.ASCII.GetString(bytes[..headerEnd]));
        var body = encoding.GetString(bytes[headerEnd..]);

        var root = new OfxNode(string.Empty);
        var stack = new Stack<OfxNode>();
        stack.Push(root);
        OfxNode? opened = null;  // abriu e ainda nao se sabe se e folha ou agregado
        OfxNode? leaf = null;    // folha recem-lida, cujo fechamento (opcional) pode vir a seguir

        var position = 0;
        while (position < body.Length)
        {
            var tagStart = body.IndexOf('<', position);
            var text = (tagStart < 0 ? body[position..] : body[position..tagStart]).Trim();

            if (text.Length > 0 && opened is not null)
            {
                // Texto logo depois da abertura: e folha, que no SGML nao precisa de fechamento.
                opened.Value = DecodeText(text);
                stack.Pop();
                leaf = opened;
                opened = null;
            }

            if (tagStart < 0)
            {
                break;
            }

            var tagEnd = body.IndexOf('>', tagStart);
            if (tagEnd < 0)
            {
                return Result.Failure<OfxNode>(ImportErrors.FormatInvalid("O OFX esta incompleto."));
            }

            var tag = body[(tagStart + 1)..tagEnd].Trim();
            position = tagEnd + 1;

            if (tag.StartsWith('!') || tag.StartsWith('?'))
            {
                return Result.Failure<OfxNode>(ImportErrors.FormatInvalid(
                    "O OFX tem declaracoes nao permitidas (DOCTYPE, entidades)."));
            }

            if (tag.StartsWith('/'))
            {
                var name = tag[1..].Trim().ToUpperInvariant();
                var closesLeaf = leaf is not null && leaf.Name == name;
                leaf = null;
                opened = null;
                if (closesLeaf)
                {
                    continue;
                }

                if (!stack.Any(n => n.Name == name))
                {
                    return Result.Failure<OfxNode>(ImportErrors.FormatInvalid("O OFX esta malformado."));
                }

                while (stack.Pop().Name != name)
                {
                }

                continue;
            }

            if (tag.Length == 0)
            {
                return Result.Failure<OfxNode>(ImportErrors.FormatInvalid("O OFX esta malformado."));
            }

            var node = new OfxNode(tag.Split(' ', 2)[0].ToUpperInvariant());
            stack.Peek().Children.Add(node);
            stack.Push(node);
            opened = node;
            leaf = null;
        }

        // Sobrou agregado aberto (ex.: </OFX> faltando): arquivo truncado.
        return stack.Count == 1
            ? root
            : Result.Failure<OfxNode>(ImportErrors.FormatInvalid("O OFX esta incompleto."));
    }

    /// <summary>Codificacao declarada no cabecalho do OFX 1.x; sem declaracao, Windows-1252 (bancos brasileiros).</summary>
    private static Encoding SgmlEncoding(string header)
    {
        var values = header
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0].Trim().ToUpperInvariant(), pair => pair[1].Trim().ToUpperInvariant());

        if (values.TryGetValue("ENCODING", out var declared) && declared is "UTF-8" or "UTF8")
        {
            return Encoding.UTF8;
        }

        return values.TryGetValue("CHARSET", out var charset) && charset is "ISO-8859-1" or "8859-1"
            ? Encoding.Latin1
            : CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
    }

    private static string DecodeText(string text)
    {
        if (!text.Contains('&'))
        {
            return text;
        }

        var decoded = text
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&apos;", "'", StringComparison.Ordinal)
            .Replace("&nbsp;", " ", StringComparison.Ordinal);

        // Referencias numericas (&#231;); qualquer outra entidade fica como texto, sem resolver.
        var builder = new StringBuilder(decoded.Length);
        var i = 0;
        while (i < decoded.Length)
        {
            if (decoded[i] == '&' && i + 2 < decoded.Length && decoded[i + 1] == '#')
            {
                var end = decoded.IndexOf(';', i);
                if (end > i + 2 && int.TryParse(decoded.AsSpan(i + 2, end - i - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var code)
                    && code is > 0 and <= 0xFFFF)
                {
                    builder.Append((char)code);
                    i = end + 1;
                    continue;
                }
            }

            builder.Append(decoded[i]);
            i++;
        }

        return builder.Replace("&amp;", "&").ToString();
    }
}
