using System.Text.Json.Nodes;
using SharpYaml.Serialization;

namespace WorkflowCore.Dashboard.Designer;

/// <summary>
/// Converts YAML to JSON without SharpYaml's object serializer. That serializer honours type tags
/// (<c>!SomeType</c>) and can create arbitrary .NET objects, which is unsafe for text pasted into a web page.
/// Here YAML is read as plain data: tags are rejected and every scalar becomes a JSON string, which is what
/// Workflow Core's own YAML loading produces too (Newtonsoft then converts strings for typed properties).
/// </summary>
internal static class SafeYaml
{
    /// <summary>Guards against alias expansion ("billion laughs") blowing up the converted document.</summary>
    private const int MaxNodes = 100_000;

    public static string ToJson(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0)
            throw new DesignerException("invalid-definition", "The YAML document is empty.");

        var count = 0;
        return Convert(stream.Documents[0].RootNode, ref count)?.ToJsonString() ?? "null";
    }

    private static JsonNode? Convert(YamlNode node, ref int count)
    {
        if (++count > MaxNodes)
            throw new DesignerException("invalid-definition", "The YAML document is too large.");
        if (!string.IsNullOrEmpty(node.Tag))
            throw new DesignerException("invalid-definition", $"YAML tags such as '{node.Tag}' are not allowed.");

        switch (node)
        {
            case YamlMappingNode mapping:
                var obj = new JsonObject();
                foreach (var (key, value) in mapping.Children)
                {
                    if (key is not YamlScalarNode { Value: { } name })
                        throw new DesignerException("invalid-definition", "Only plain keys are allowed in YAML mappings.");
                    obj[name] = Convert(value, ref count);
                }
                return obj;

            case YamlSequenceNode sequence:
                var array = new JsonArray();
                foreach (var item in sequence.Children)
                    array.Add(Convert(item, ref count));
                return array;

            case YamlScalarNode scalar:
                var plain = scalar.Style is SharpYaml.ScalarStyle.Plain or SharpYaml.ScalarStyle.Any;
                if (plain && scalar.Value is null or "" or "~" or "null" or "Null" or "NULL")
                    return null;
                return JsonValue.Create(scalar.Value);

            default:
                throw new DesignerException("invalid-definition", "Unsupported YAML content.");
        }
    }
}
