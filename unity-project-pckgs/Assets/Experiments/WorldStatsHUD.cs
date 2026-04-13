using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Experiments
{
    /// <summary>
    /// Read-only overlay. Reads values from <see cref="WorldStatsAspect"/> and renders
    /// them in the top-right. Stats are ticked by <see cref="WorldStatsLogic"/>.
    /// </summary>
    public class WorldStatsHUD : EntityComponent
    {
        [Aspect] private readonly WorldStatsAspect _stats = default!;

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.UpperLeft
            };

            var rect = new Rect(Screen.width - 230, 10, 220, 92);
            GUI.Box(rect, "");
            GUI.Label(new Rect(rect.x + 8, rect.y + 4, rect.width - 16, rect.height - 8),
                "<b>World Stats</b>\n" +
                $"Time: {_stats.ElapsedSeconds.Value:F1}s\n" +
                $"Entities: {_stats.EntitiesAlive.Value}\n" +
                $"Total HP: {_stats.TotalHealth.Value:F0}\n" +
                $"Damage events: {_stats.TotalDamageEvents.Value}",
                style);
        }
    }
}
