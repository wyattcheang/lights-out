// Keyboard + gamepad input through the Input System package.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace LightsOut
{
    public class InputReader
    {
        float steer;
        public DriverInput Read(float dt)
        {
            var d = new DriverInput();
            var kb = Keyboard.current; var gp = Gamepad.current;
            float target = 0;
            if (kb != null) { if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) target -= 1; if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) target += 1; }
            if (gp != null && Mathf.Abs(gp.leftStick.x.ReadValue()) > .08f) target = gp.leftStick.x.ReadValue();
            steer = target; d.Steer = steer;
            float thr = 0, brk = 0;
            if (kb != null) { if (kb.upArrowKey.isPressed || kb.wKey.isPressed) thr = 1; if (kb.downArrowKey.isPressed || kb.sKey.isPressed) brk = 1; d.Handbrake = kb.spaceKey.isPressed; d.Boost = kb.bKey.isPressed; }
            if (gp != null) { thr = Mathf.Max(thr, gp.rightTrigger.ReadValue()); brk = Mathf.Max(brk, gp.leftTrigger.ReadValue()); d.Boost |= gp.buttonWest.isPressed; d.Handbrake |= gp.buttonEast.isPressed; }
            d.Throttle = thr; d.Brake = brk;
            return d;
        }
        public static bool Pressed(Key k) { var kb = Keyboard.current; return kb != null && kb[k].wasPressedThisFrame; }
        public static bool PadPressed(System.Func<Gamepad, ButtonControl> f) { var gp = Gamepad.current; return gp != null && f(gp).wasPressedThisFrame; }
    }
}
