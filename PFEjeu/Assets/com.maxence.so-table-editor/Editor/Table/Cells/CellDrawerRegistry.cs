using System;
using System.Collections.Generic;
using UnityEditor;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Chooses the drawer of each cell. Custom drawers are checked first (last registered wins),
    /// then the built-in complex drawer, then the default one.
    /// Register custom drawers from an [InitializeOnLoad] static constructor: the list is
    /// cleared on every domain reload.
    /// </summary>
    public static class CellDrawerRegistry
    {
        private static readonly List<ICellDrawer> CustomDrawers = new List<ICellDrawer>();
        private static readonly ICellDrawer ComplexDrawer = new ComplexCellDrawer();
        private static readonly ICellDrawer DefaultDrawer = new DefaultCellDrawer();

        public static void Register(ICellDrawer drawer)
        {
            if (drawer == null)
            {
                throw new ArgumentNullException(nameof(drawer));
            }

            if (!CustomDrawers.Contains(drawer))
            {
                CustomDrawers.Insert(0, drawer);
            }
        }

        public static void Unregister(ICellDrawer drawer)
        {
            CustomDrawers.Remove(drawer);
        }

        internal static ICellDrawer Resolve(SerializedProperty property)
        {
            for (int i = 0; i < CustomDrawers.Count; i++)
            {
                if (CustomDrawers[i].CanDraw(property))
                {
                    return CustomDrawers[i];
                }
            }

            return ComplexDrawer.CanDraw(property) ? ComplexDrawer : DefaultDrawer;
        }
    }
}
