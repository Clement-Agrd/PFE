using UnityEngine;
using UnityEngine.InputSystem;

namespace Core
{
    /// <summary>
    /// Action « fermer l'UI affichée » (E, Échap, bouton Est de la manette) via le nouvel Input System.
    /// À appeler depuis Update() d'un panneau ouvert, en passant la frame d'ouverture :
    /// la frame d'ouverture est ignorée, car c'est souvent la même touche (E) qui vient d'ouvrir le panneau.
    /// </summary>
    public static class UiCloseKeys
    {
        private static InputAction _close;

        public static bool Pressed(int openedFrame)
        {
            if (_close == null)
            {
                _close = new InputAction("CloseUI", InputActionType.Button);
                _close.AddBinding("<Keyboard>/escape");
                _close.AddBinding("<Keyboard>/e");
                _close.AddBinding("<Gamepad>/buttonEast");
            }
            if (!_close.enabled) _close.Enable();

            return Time.frameCount != openedFrame && _close.WasPressedThisFrame();
        }
    }
}
