using System.Collections.Generic;
using System.Drawing;

namespace ColorThiefDotNet
{
    /// <summary>
    ///     Color map
    /// </summary>
#if NETCOREAPP && NET7_0_OR_GREATER
    internal struct CMap
#else
    internal class CMap
#endif
    {
        private readonly List<VBox> vboxes = new List<VBox>();
        private List<QuantizedColor> palette;

        public CMap() { }

        public void Push(VBox box)
        {
            palette = null;
            vboxes.Add(box);
        }

        public IEnumerable<QuantizedColor> GeneratePalette()
        {
            foreach (VBox vbox in vboxes)
            {
                int rgb = vbox.Avg(false);
                Color color = Color.FromArgb((byte)(rgb >> 16 & 0xFF), (byte)(rgb >> 8 & 0xFF), (byte)(rgb >> 0 & 0xFF));
                yield return new QuantizedColor(color, vbox.Count(false));
            }
        }

        public List<QuantizedColor> GeneratePaletteList()
        {
            if (palette == null)
            {
                palette = new List<QuantizedColor>();
                foreach (VBox vbox in vboxes)
                {
                    int rgb = vbox.Avg(false);
                    Color color = Color.FromArgb((byte)(rgb >> 16 & 0xFF), (byte)(rgb >> 8 & 0xFF), (byte)(rgb >> 0 & 0xFF));
                    palette.Add(new QuantizedColor(color, vbox.Count(false)));
                }
            }

            return palette;
        }
    }
}