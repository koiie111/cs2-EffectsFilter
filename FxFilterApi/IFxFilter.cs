namespace FxFilterApi;

/// <summary>Categories of other players' cosmetic effects a viewer can hide with !fx.</summary>
public enum FxCategory { Trails, Auras, C4, Kill }

/// <summary>
/// Shared contract of 4ill.EffectsFilter, published as the "4ill:fx" plugin capability.
/// Effect modules call it from their CheckTransmit; when the capability is missing they keep their old behavior.
/// </summary>
public interface IFxFilter
{
    /// <summary>True if the viewer hides other players' effects of this category. Own effects are never hidden.</summary>
    bool IsHiddenFor(int viewerSlot, FxCategory category);

    /// <summary>True if the category is switched off on this server (fx_disable_* cvar): the effect is not created at all.</summary>
    bool IsDisabledOnServer(FxCategory category);
}
