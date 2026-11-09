using System;
using System.Linq;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin;

namespace ClickAndTap
{
    [PluginName("Click and Tap")]
    public class ClickAndTapFilter : IPositionedPipelineElement<IDeviceReport>
    {
        public PipelinePosition Position => PipelinePosition.PostTransform;
        public event Action<IDeviceReport>? Emit;

        private enum PenState
        {
            Hover,
            Contact,
            BoundContact,
            SuppressedContact
        }

        private PenState _state = PenState.Hover;
        private bool _wasButtonPressed;
        private bool[]? _bufferedButtons;

        public void Consume(IDeviceReport report)
        {
            if (report is not ITabletReport tabletReport)
            {
                Emit?.Invoke(report);
                return;
            }

            bool isPenDown = tabletReport.Pressure > 0f;
            bool isButtonPressed = tabletReport.PenButtons.Any(b => b);

            switch (_state)
            {
                case PenState.Hover:
                    HandleHover(tabletReport, isPenDown, isButtonPressed);
                    break;
                case PenState.Contact:
                    HandleContact(tabletReport, isPenDown);
                    break;
                case PenState.BoundContact:
                    HandleBoundContact(tabletReport, isPenDown, isButtonPressed);
                    break;
                case PenState.SuppressedContact:
                    HandleSuppressedContact(tabletReport, isPenDown);
                    break;
            }

            _wasButtonPressed = isButtonPressed;
        }

        private void HandleHover(ITabletReport report, bool isPenDown, bool isButtonPressed)
        {
            if (!isPenDown)
            {
                if (isButtonPressed != _wasButtonPressed)
                {
                    _bufferedButtons = isButtonPressed ? (bool[])report.PenButtons.Clone() : null;
                }

                EmitReport(report, pressure: 0u, buttons: null);
                return;
            }

            bool shouldUseBinding = isButtonPressed || _bufferedButtons != null;
            if (shouldUseBinding)
            {
                bool[] buttonsToUse = isButtonPressed ? report.PenButtons : _bufferedButtons!;
                EmitClickBinding(report, buttonsToUse);
                _bufferedButtons = (bool[])buttonsToUse.Clone();
                _state = PenState.BoundContact;
            }
            else
            {
                Emit?.Invoke(report);
                _state = PenState.Contact;
            }
        }

        private void HandleContact(ITabletReport report, bool isPenDown)
        {
            if (isPenDown)
            {
                Emit?.Invoke(report);
                return;
            }

            // A normal contact ends naturally; do not alter its release report.
            Emit?.Invoke(report);
            ResetToHover();
        }

        private void HandleBoundContact(ITabletReport report, bool isPenDown, bool isButtonPressed)
        {
            if (!isPenDown)
            {
                EmitReport(report, pressure: 0u, buttons: null);
                ResetToHover();
                return;
            }

            if (_wasButtonPressed && !isButtonPressed)
            {
                // End the binding but suppress contact until the pen is lifted,
                // otherwise the remaining pen-down reports can become left-clicks.
                EmitReport(report, pressure: 0u, buttons: null);
                _state = PenState.SuppressedContact;
                ClearBufferedState();
                return;
            }

            EmitDragWithBinding(report);
        }

        private void HandleSuppressedContact(ITabletReport report, bool isPenDown)
        {
            // Keep pressure and buttons released until the pen leaves the surface.
            EmitReport(report, pressure: 0u, buttons: null);
            if (!isPenDown)
                ResetToHover();
        }

        private void EmitClickBinding(ITabletReport report, bool[] buttons)
        {
            bool[] mappedButtons = new bool[report.PenButtons.Length];

            // Map physical buttons: first -> right click, second -> middle click
            if (buttons.Length > 0 && buttons[0])
                mappedButtons[0] = true;
            else if (buttons.Length > 1 && buttons[1])
                mappedButtons[1] = true;

            EmitReport(report, pressure: 0u, mappedButtons);
        }

        private void EmitDragWithBinding(ITabletReport report)
        {
            if (_bufferedButtons == null)
                return;

            bool[] mappedButtons = new bool[report.PenButtons.Length];

            if (_bufferedButtons.Length > 0 && _bufferedButtons[0])
                mappedButtons[0] = true;
            else if (_bufferedButtons.Length > 1 && _bufferedButtons[1])
                mappedButtons[1] = true;

            EmitReport(report, pressure: 0u, mappedButtons);
        }

        private void EmitReport(ITabletReport original, uint pressure, bool[]? buttons)
        {
            var emittedReport = new TabletReport
            {
                Position = original.Position,
                Pressure = pressure,
                PenButtons = buttons ?? new bool[original.PenButtons.Length]
            };

            Emit?.Invoke(emittedReport);
        }

        private void ResetToHover()
        {
            _state = PenState.Hover;
            ClearBufferedState();
        }

        private void ClearBufferedState()
        {
            _bufferedButtons = null;
        }
    }
}
