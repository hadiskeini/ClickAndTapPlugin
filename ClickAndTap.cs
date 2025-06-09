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

        private bool _wasPenDown;
        private bool _wasButtonPressed;
        private bool[]? _bufferedButtons;
        private bool _isBindingActive;

        public void Consume(IDeviceReport report)
        {
            if (report is not ITabletReport tabletReport)
            {
                Emit?.Invoke(report);
                return;
            }

            bool isPenDown = tabletReport.Pressure > 0f;
            bool isButtonPressed = tabletReport.PenButtons.Any(b => b);

            // Handle state transitions
            if (_wasPenDown && !isPenDown)
            {
                HandlePenLift(tabletReport);
            }
            else if (!isPenDown)
            {
                HandleHoverState(tabletReport, isButtonPressed);
            }
            else if (isPenDown)
            {
                HandlePenDownState(tabletReport, isButtonPressed);
            }

            // Update state
            _wasPenDown = isPenDown;
            _wasButtonPressed = isButtonPressed;
        }

        private void HandlePenLift(ITabletReport report)
        {
            if (_isBindingActive)
            {
                EmitReport(report, pressure: 0u, buttons: null);
                _isBindingActive = false;
            }
            else
            {
                Emit?.Invoke(report);
            }
            
            ClearBufferedState();
        }

        private void HandleHoverState(ITabletReport report, bool isButtonPressed)
        {
            // Update buffered buttons on state change
            if (isButtonPressed != _wasButtonPressed)
            {
                _bufferedButtons = isButtonPressed ? (bool[])report.PenButtons.Clone() : null;
            }

            // Emit position-only report
            EmitReport(report, pressure: 0u, buttons: null);
        }

        private void HandlePenDownState(ITabletReport report, bool isButtonPressed)
        {
            bool isPenTouchDown = !_wasPenDown;
            
            if (isPenTouchDown)
            {
                HandlePenTouchDown(report, isButtonPressed);
                return;
            }

            // Handle pen drag
            if (_isBindingActive)
            {
                EmitDragWithBinding(report);
            }
            else
            {
                Emit?.Invoke(report);
            }

            // Handle button release during active binding
            if (_isBindingActive && _wasButtonPressed && !isButtonPressed)
            {
                EmitReport(report, pressure: 0u, buttons: null);
                _isBindingActive = false;
                ClearBufferedState();
            }
        }

        private void HandlePenTouchDown(ITabletReport report, bool isButtonPressed)
        {
            bool shouldUseBinding = isButtonPressed || _bufferedButtons != null;
            
            if (shouldUseBinding)
            {
                bool[] buttonsToUse = isButtonPressed ? report.PenButtons : _bufferedButtons!;
                EmitClickBinding(report, buttonsToUse);
                _bufferedButtons = (bool[])buttonsToUse.Clone();
                _isBindingActive = true;
            }
            else
            {
                Emit?.Invoke(report);
            }
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
            if (_bufferedButtons == null) return;

            bool[] mappedButtons = new bool[report.PenButtons.Length];
            
            if (_bufferedButtons.Length > 0 && _bufferedButtons[0])
                mappedButtons[0] = true;
            else if (_bufferedButtons.Length > 1 && _bufferedButtons[1])
                mappedButtons[1] = true;

            EmitReport(report, pressure: 0u, mappedButtons);
        }

        private void EmitReport(ITabletReport original, uint pressure, bool[]? buttons)
        {
            var report = new TabletReport
            {
                Position = original.Position,
                Pressure = pressure,
                PenButtons = buttons ?? new bool[original.PenButtons.Length]
            };
            
            Emit?.Invoke(report);
        }

        private void ClearBufferedState()
        {
            _bufferedButtons = null;
        }
    }
}