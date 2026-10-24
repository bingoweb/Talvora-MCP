namespace Talvora.Tray.ViewModels;

internal enum ComponentVisualState
{
    Ready,
    Attention,
    Offline,
    Neutral,
    Repairing,
}

internal sealed record StatusNarratorEntry(
    string Name,
    ComponentVisualState State,
    bool IsOptional = false,
    string PlainReason = "");

internal sealed record StatusNarration(string Title, string Detail);

/// <summary>DESIGN-CONTROL-CENTER §9's approved, human-readable snapshot copy.</summary>
internal static class StatusNarrator
{
    public static StatusNarration Build(
        IReadOnlyList<StatusNarratorEntry> components,
        DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(components);
        var unreachable = components.Count == 0 ||
            components.Any(component => component.State == ComponentVisualState.Offline &&
                (component.Name.Contains("Talvora Çekirdeği", StringComparison.OrdinalIgnoreCase) ||
                 component.Name.Equals("Talvora", StringComparison.OrdinalIgnoreCase)));
        if (unreachable)
            return new StatusNarration(
                "Talvora hizmetine şu anda ulaşılamıyor. Bilgisayarınızdaki dosyalar etkilenmedi.",
                string.Empty);

        var activeProblems = components
            .Where(component => !component.IsOptional && component.State is
                ComponentVisualState.Attention or ComponentVisualState.Offline)
            .OrderBy(component => component.State == ComponentVisualState.Offline ? 0 : 1)
            .ToArray();
        var ready = components.Count(component => component.State == ComponentVisualState.Ready);
        var total = components.Count;

        if (activeProblems.Length > 1)
            return new StatusNarration(
                $"{activeProblems.Length} bileşen dikkat istiyor. En önemlisi: {activeProblems[0].Name}.",
                $"{ready}/{total} bileşen hazır.");

        if (activeProblems.Length == 1)
        {
            var problem = activeProblems[0];
            var reason = string.IsNullOrWhiteSpace(problem.PlainReason)
                ? "Dikkat istiyor." : problem.PlainReason;
            return new StatusNarration($"{problem.Name} dikkat istiyor: {reason}",
                $"{ready}/{total} bileşen hazır.");
        }

        if (components.FirstOrDefault(component =>
            component.State == ComponentVisualState.Repairing) is { } repairing)
            return new StatusNarration(
                $"{repairing.Name} onarılıyor… ({repairing.PlainReason})",
                $"{ready}/{total} bileşen hazır.");

        var optionalOff = components.FirstOrDefault(component =>
            component.IsOptional && component.State == ComponentVisualState.Neutral);
        if (optionalOff is not null)
            return new StatusNarration("Her şey yolunda.",
                $"{ready}/{total} bileşen hazır; {optionalOff.Name} isteğe bağlı olarak kapalı.");

        return new StatusNarration("Her şey yolunda.",
            $"{ready} bileşenin hepsi hazır. Son kontrol {checkedAt.ToLocalTime():HH:mm}.");
    }
}
