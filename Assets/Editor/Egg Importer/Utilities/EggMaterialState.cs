using System;
using System.Collections.Generic;
using System.Globalization;

public enum EggAlphaMode
{
    Unspecified,
    Off,
    On,
    Blend,
    BlendNoOcclude,
    Ms,
    MsMask,
    Binary,
    Dual,
    Premultiplied
}

public class EggRenderState
{
    public EggAlphaMode AlphaMode = EggAlphaMode.Unspecified;
    public bool? DepthWrite = null;
    public int? DrawOrder = null;
    public string Bin = null;

    public EggRenderState Clone()
    {
        return new EggRenderState
        {
            AlphaMode = AlphaMode,
            DepthWrite = DepthWrite,
            DrawOrder = DrawOrder,
            Bin = Bin
        };
    }
}

public class EggMaterialState
{
    public EggAlphaMode AlphaMode = EggAlphaMode.Off;
    public bool DepthWrite = true;
    public int DrawOrder = 0;
    public string Bin = null;

    public bool IsTransparent
    {
        get
        {
            return AlphaMode == EggAlphaMode.Blend ||
                   AlphaMode == EggAlphaMode.BlendNoOcclude ||
                   AlphaMode == EggAlphaMode.Dual ||
                   AlphaMode == EggAlphaMode.Premultiplied;
        }
    }

    public bool UsesAlphaTest
    {
        get
        {
            return AlphaMode == EggAlphaMode.Binary ||
                   AlphaMode == EggAlphaMode.Ms ||
                   AlphaMode == EggAlphaMode.MsMask;
        }
    }

    public bool IsDefault
    {
        get { return AlphaMode == EggAlphaMode.Off && DepthWrite && DrawOrder == 0 && string.IsNullOrEmpty(Bin); }
    }

    public string ToKey()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "alpha={0};zwrite={1};order={2};bin={3}",
            EggMaterialRenderState.AlphaModeToEggValue(AlphaMode),
            DepthWrite ? "1" : "0",
            DrawOrder,
            string.IsNullOrEmpty(Bin) ? "default" : Bin);
    }
}

public static class EggMaterialRenderState
{
    public const string Marker = "__EGGSTATE__";

    public static string ReadScalarValue(string line)
    {
        int openBrace = line.IndexOf('{');
        int closeBrace = line.LastIndexOf('}');
        if (openBrace < 0 || closeBrace <= openBrace) return string.Empty;
        return line.Substring(openBrace + 1, closeBrace - openBrace - 1).Trim().Trim('"');
    }

    public static bool ApplyScalarLine(EggRenderState state, string line)
    {
        if (state == null || string.IsNullOrEmpty(line) || !line.StartsWith("<Scalar>", StringComparison.Ordinal))
        {
            return false;
        }

        string lowerLine = line.ToLowerInvariant();
        string value = ReadScalarValue(line);

        if (lowerLine.StartsWith("<scalar> alpha", StringComparison.Ordinal))
        {
            EggAlphaMode mode;
            if (TryParseAlphaMode(value, out mode))
            {
                state.AlphaMode = mode;
                return true;
            }
        }
        else if (lowerLine.StartsWith("<scalar> depth_write", StringComparison.Ordinal) ||
                 lowerLine.StartsWith("<scalar> depth-write", StringComparison.Ordinal))
        {
            bool parsed;
            if (TryParseBool(value, out parsed))
            {
                state.DepthWrite = parsed;
                return true;
            }
        }
        else if (lowerLine.StartsWith("<scalar> draw-order", StringComparison.Ordinal) ||
                 lowerLine.StartsWith("<scalar> draw_order", StringComparison.Ordinal))
        {
            int order;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out order))
            {
                state.DrawOrder = order;
                return true;
            }
        }
        else if (lowerLine.StartsWith("<scalar> bin", StringComparison.Ordinal))
        {
            state.Bin = value;
            return true;
        }

        return false;
    }

    public static bool TryParseAlphaMode(string value, out EggAlphaMode mode)
    {
        mode = EggAlphaMode.Unspecified;
        if (string.IsNullOrEmpty(value)) return false;

        string normalized = value.Trim().ToLowerInvariant().Replace("-", "_");
        switch (normalized)
        {
            case "off":
                mode = EggAlphaMode.Off;
                return true;
            case "on":
                mode = EggAlphaMode.On;
                return true;
            case "blend":
                mode = EggAlphaMode.Blend;
                return true;
            case "blend_no_occlude":
                mode = EggAlphaMode.BlendNoOcclude;
                return true;
            case "ms":
                mode = EggAlphaMode.Ms;
                return true;
            case "ms_mask":
                mode = EggAlphaMode.MsMask;
                return true;
            case "binary":
                mode = EggAlphaMode.Binary;
                return true;
            case "dual":
                mode = EggAlphaMode.Dual;
                return true;
            case "premultiplied":
                mode = EggAlphaMode.Premultiplied;
                return true;
            default:
                return false;
        }
    }

    public static string AlphaModeToEggValue(EggAlphaMode mode)
    {
        switch (mode)
        {
            case EggAlphaMode.On: return "on";
            case EggAlphaMode.Blend: return "blend";
            case EggAlphaMode.BlendNoOcclude: return "blend_no_occlude";
            case EggAlphaMode.Ms: return "ms";
            case EggAlphaMode.MsMask: return "ms_mask";
            case EggAlphaMode.Binary: return "binary";
            case EggAlphaMode.Dual: return "dual";
            case EggAlphaMode.Premultiplied: return "premultiplied";
            case EggAlphaMode.Off:
            case EggAlphaMode.Unspecified:
            default:
                return "off";
        }
    }

    public static EggMaterialState Resolve(EggRenderState renderState, IList<string> textureRefs, IDictionary<string, TextureWrapData> textureData, bool hasVertexAlpha)
    {
        EggAlphaMode requestedMode = renderState != null ? renderState.AlphaMode : EggAlphaMode.Unspecified;
        bool binaryAlphaOnly = true;
        bool hasTextureAlpha = false;

        if (textureRefs != null && textureData != null)
        {
            for (int i = 0; i < textureRefs.Count; i++)
            {
                TextureWrapData texture;
                if (!textureData.TryGetValue(textureRefs[i], out texture) || !TextureAffectsPolygonAlpha(texture.envType))
                {
                    continue;
                }

                if (requestedMode == EggAlphaMode.Unspecified && texture.alphaMode != EggAlphaMode.Unspecified)
                {
                    requestedMode = texture.alphaMode;
                }

                if (TextureHasAlpha(texture))
                {
                    hasTextureAlpha = true;
                    if (!FormatHasBinaryAlpha(texture.format))
                    {
                        binaryAlphaOnly = false;
                    }
                }
            }
        }

        if (hasVertexAlpha)
        {
            binaryAlphaOnly = false;
        }

        if (requestedMode == EggAlphaMode.Unspecified && (hasTextureAlpha || hasVertexAlpha))
        {
            requestedMode = EggAlphaMode.On;
        }

        EggAlphaMode resolvedMode;
        switch (requestedMode)
        {
            case EggAlphaMode.Off:
            case EggAlphaMode.Unspecified:
                resolvedMode = EggAlphaMode.Off;
                break;
            case EggAlphaMode.On:
                resolvedMode = binaryAlphaOnly ? EggAlphaMode.Binary : EggAlphaMode.Blend;
                break;
            default:
                resolvedMode = requestedMode;
                break;
        }

        bool depthWrite = resolvedMode == EggAlphaMode.BlendNoOcclude ? false : true;
        if (renderState != null && renderState.DepthWrite.HasValue)
        {
            depthWrite = renderState.DepthWrite.Value;
        }

        return new EggMaterialState
        {
            AlphaMode = resolvedMode,
            DepthWrite = depthWrite,
            DrawOrder = renderState != null && renderState.DrawOrder.HasValue ? renderState.DrawOrder.Value : 0,
            Bin = renderState != null ? renderState.Bin : null
        };
    }

    public static string AppendState(string materialName, EggMaterialState state)
    {
        if (state == null || state.IsDefault || string.IsNullOrEmpty(materialName))
        {
            return materialName;
        }

        return materialName + Marker + state.ToKey();
    }

    public static bool TryDecodeMaterialName(string materialName, out string cleanMaterialName, out EggMaterialState state)
    {
        state = new EggMaterialState();
        cleanMaterialName = materialName;

        if (string.IsNullOrEmpty(materialName))
        {
            return false;
        }

        int markerIndex = materialName.IndexOf(Marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        cleanMaterialName = materialName.Substring(0, markerIndex);
        string key = materialName.Substring(markerIndex + Marker.Length);
        string[] parts = key.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            string[] pair = parts[i].Split(new[] { '=' }, 2);
            if (pair.Length != 2) continue;

            string name = pair[0].Trim().ToLowerInvariant();
            string value = pair[1].Trim();

            if (name == "alpha")
            {
                EggAlphaMode mode;
                if (TryParseAlphaMode(value, out mode))
                {
                    state.AlphaMode = mode == EggAlphaMode.On ? EggAlphaMode.Blend : mode;
                }
            }
            else if (name == "zwrite")
            {
                bool parsed;
                if (TryParseBool(value, out parsed))
                {
                    state.DepthWrite = parsed;
                }
            }
            else if (name == "order")
            {
                int order;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out order))
                {
                    state.DrawOrder = order;
                }
            }
            else if (name == "bin" && value != "default")
            {
                state.Bin = value;
            }
        }

        return true;
    }

    public static bool TextureAffectsPolygonAlpha(string envType)
    {
        if (string.IsNullOrEmpty(envType)) return true;

        string normalized = envType.Trim().ToLowerInvariant().Replace("-", "_");
        return normalized == "modulate" || normalized == "replace";
    }

    public static bool TextureHasAlpha(TextureWrapData texture)
    {
        return texture != null && (texture.hasAlphaFile || FormatAffectsAlpha(texture.format));
    }

    public static bool FormatAffectsAlpha(string format)
    {
        if (string.IsNullOrEmpty(format)) return false;

        string normalized = format.Trim().ToLowerInvariant().Replace("-", "_");
        switch (normalized)
        {
            case "red":
            case "green":
            case "blue":
            case "luminance":
            case "sluminance":
            case "rgb":
            case "rgb12":
            case "rgb8":
            case "rgb5":
            case "rgb332":
            case "srgb":
                return false;
            default:
                return normalized.Contains("alpha") ||
                       normalized == "a" ||
                       normalized == "rgba" ||
                       normalized == "rgba12" ||
                       normalized == "rgba8" ||
                       normalized == "rgba4" ||
                       normalized == "rgba5" ||
                       normalized == "rgbm";
        }
    }

    public static bool FormatHasBinaryAlpha(string format)
    {
        return !string.IsNullOrEmpty(format) && format.Trim().Equals("rgbm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseBool(string value, out bool parsed)
    {
        parsed = false;
        if (string.IsNullOrEmpty(value)) return false;

        string normalized = value.Trim().ToLowerInvariant();
        if (normalized == "1" || normalized == "true" || normalized == "yes" || normalized == "on")
        {
            parsed = true;
            return true;
        }

        if (normalized == "0" || normalized == "false" || normalized == "no" || normalized == "off")
        {
            parsed = false;
            return true;
        }

        return false;
    }
}
