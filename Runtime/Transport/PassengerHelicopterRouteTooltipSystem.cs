using Game.UI.Localization;
using Game.UI.Tooltip;

namespace CityWeaver
{
    public partial class PassengerHelicopterRouteTooltipSystem : TooltipSystemBase
    {
        private readonly StringTooltip m_Tooltip = new StringTooltip { path = "cityWeaverHelicopterRoutePolicy" };
        protected override void OnUpdate()
        {
            var error = World.GetExistingSystemManaged<PassengerHelicopterRouteToolPolicySystem>()?.LastError;
            var key = HelicopterLocale.RouteErrorKey(error);
            if (key == null) return;
            m_Tooltip.value = LocalizedString.Id(key);
            AddMouseTooltip(m_Tooltip);
        }
    }
}
