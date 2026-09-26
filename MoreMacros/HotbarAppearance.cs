using Intermediate = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarUIIntermediate;

namespace MoreMacros;

internal static unsafe class HotbarAppearance
{
    public static void Apply(Intermediate* data, bool available)
    {
        data->ActionAvailable1 = available;
        data->ActionAvailable2 = available;
        data->ActionTargetSatisfied = true;
        // This maps to ActionBarSlotNumberArray.Pulses, not general availability.
        data->IsTransformationActionUsable = false;
        data->CostValue = 0;
        data->CostDisplayMode = 0;
        data->CooldownMode = data->CooldownSeconds = data->CooldownPercent = data->LastCooldownPercent = 0;
        data->ChargePercent = data->LastChargePercent = data->CurrentCharges = 0;
        data->DrawAnts = false;
    }
}
