using System;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public static class TooltipExtensions
    {
        public static TooltipManipulator AddTooltip(
            this VisualElement element, TooltipService service, string text, float delay = 0.3f)
        {
            var manipulator = new TooltipManipulator(service, text, delay);
            element.AddManipulator(manipulator);
            return manipulator;
        }

        public static TooltipManipulator AddTooltip(
            this VisualElement element, TooltipService service, Func<string> textFactory,
            float delay = 0.3f)
        {
            var manipulator = new TooltipManipulator(service, textFactory, delay);
            element.AddManipulator(manipulator);
            return manipulator;
        }

        public static TooltipManipulator AddTooltip(
            this VisualElement element, TooltipService service, Func<VisualElement> contentFactory,
            float delay = 0.3f)
        {
            var manipulator = new TooltipManipulator(service, contentFactory, delay);
            element.AddManipulator(manipulator);
            return manipulator;
        }

        public static void RemoveTooltip(this VisualElement element, TooltipManipulator manipulator)
        {
            element.RemoveManipulator(manipulator);
        }
    }
}
