using System;
using System.Collections.Generic;
#if !NETCOREAPP
using System.Linq;
#endif

namespace ColorThiefDotNet
{
    internal ref struct VBoxesRef
    {
        public VBoxesRef(VBox vbox1, VBox vbox2)
        {
            VBox1 = vbox1;
            VBox2 = vbox2;
        }

        public readonly VBox VBox1;
        public readonly VBox VBox2;
    }

    internal static class Mmcq
    {
        public const int Sigbits = 5;
        public const int Rshift = 8 - Sigbits;
        public const int Mult = 1 << Rshift;
        public const int Histosize = 1 << (3 * Sigbits);
        public const int VboxLength = 1 << Sigbits;
        public const double FractByPopulation = 0.75;
        public const int MaxIterations = 1000;
        public const double WeightSaturation = 3d;
        public const double WeightLuma = 6d;
        public const double WeightPopulation = 1d;

#if !NETCOREAPP
        private static readonly int[] EmptyVboxLengthArray = Enumerable.Repeat(-1, VboxLength).ToArray();
#endif
        private static readonly VBoxComparer ComparatorProduct = new VBoxComparer();
        private static readonly VBoxCountComparer ComparatorCount = new VBoxCountComparer();

        public static int GetColorIndex(int r, int g, int b) => (r << (2 * Sigbits)) + (g << Sigbits) + b;

#if NETCOREAPP
        private static VBoxesRef DoCut(int color, VBox vbox, ReadOnlySpan<int> partialsum, ReadOnlySpan<int> lookaheadsum, int total)
#else
        private static VBoxesRef DoCut(int color, VBox vbox, in int[] partialsum, in int[] lookaheadsum, int total)
#endif
        {
            int vboxDim1;
            int vboxDim2;

            switch (color)
            {
                case 0:
                    vboxDim1 = vbox.R1;
                    vboxDim2 = vbox.R2;
                    break;
                case 1:
                    vboxDim1 = vbox.G1;
                    vboxDim2 = vbox.G2;
                    break;
                default:
                    vboxDim1 = vbox.B1;
                    vboxDim2 = vbox.B2;
                    break;
            }

            for (int i = vboxDim1; i <= vboxDim2; i++)
            {
                if (partialsum[i] > total / 2)
                {
                    VBox vbox1 = vbox.Clone();
                    VBox vbox2 = vbox.Clone();

                    int left = i - vboxDim1;
                    int right = vboxDim2 - i;

                    int d2 = left <= right
                        ? Math.Min(vboxDim2 - 1, Math.Abs(i + right / 2))
                        : Math.Max(vboxDim1, Math.Abs((int)(i - 1 - left / 2.0)));

                    // avoid 0-count boxes
                    while (d2 < 0 || partialsum[d2] <= 0)
                    {
                        d2++;
                    }
                    int count2 = lookaheadsum[d2];
                    while (count2 == 0 && d2 > 0 && partialsum[d2 - 1] > 0)
                    {
                        count2 = lookaheadsum[--d2];
                    }

                    // set dimensions
                    switch (color)
                    {
                        case 0:
                            vbox1.R2 = d2;
                            vbox2.R1 = d2 + 1;
                            break;
                        case 1:
                            vbox1.G2 = d2;
                            vbox2.G1 = d2 + 1;
                            break;
                        default:
                            vbox1.B2 = d2;
                            vbox2.B1 = d2 + 1;
                            break;
                    }
                    vbox1.count = partialsum[d2];
                    vbox2.count = lookaheadsum[d2];

                    return new VBoxesRef(vbox1, vbox2);
                }
            }

            throw new Exception("VBox can't be cut");
        }

#if NETCOREAPP
        private static VBoxesRef MedianCutApply(ReadOnlySpan<int> histo, VBox vbox)
#else
        private static VBoxesRef MedianCutApply(in int[] histo, VBox vbox)
#endif
        {
            // only one pixel, no split

            int rw = vbox.R2 - vbox.R1 + 1;
            int gw = vbox.G2 - vbox.G1 + 1;
            int bw = vbox.B2 - vbox.B1 + 1;
            int maxw = Math.Max(Math.Max(rw, gw), bw);

            // Find the partial sum arrays along the selected axis.
            int total = 0;

            // -1 = not set / 0 = 0
#if NETCOREAPP
            Span<int> partialsum = new int[VboxLength];
            partialsum.Fill(-1);
#else
            int[] partialsum = new int[VboxLength];
            Array.Copy(EmptyVboxLengthArray, partialsum, VboxLength);
#endif


            // -1 = not set / 0 = 0
#if NETCOREAPP
            Span<int> lookaheadsum = new int[VboxLength];
            lookaheadsum.Fill(-1);
#else
            int[] lookaheadsum = new int[VboxLength];
            Array.Copy(EmptyVboxLengthArray, lookaheadsum, VboxLength);
#endif

            int i, j, k, sum, index;

            if (maxw == rw)
            {
                for (i = vbox.R1; i <= vbox.R2; i++)
                {
                    sum = 0;
                    for (j = vbox.G1; j <= vbox.G2; j++)
                    {
                        for (k = vbox.B1; k <= vbox.B2; k++)
                        {
                            index = GetColorIndex(i, j, k);
                            sum += histo[index];
                        }
                    }
                    total += sum;
                    partialsum[i] = total;
                }
            }
            else if (maxw == gw)
            {
                for (i = vbox.G1; i <= vbox.G2; i++)
                {
                    sum = 0;
                    for (j = vbox.R1; j <= vbox.R2; j++)
                    {
                        for (k = vbox.B1; k <= vbox.B2; k++)
                        {
                            index = GetColorIndex(j, i, k);
                            sum += histo[index];
                        }
                    }
                    total += sum;
                    partialsum[i] = total;
                }
            }
            else /* maxw == bw */
            {
                for (i = vbox.B1; i <= vbox.B2; i++)
                {
                    sum = 0;
                    for (j = vbox.R1; j <= vbox.R2; j++)
                    {
                        for (k = vbox.G1; k <= vbox.G2; k++)
                        {
                            index = GetColorIndex(j, k, i);
                            sum += histo[index];
                        }
                    }
                    total += sum;
                    partialsum[i] = total;
                }
            }

            for (i = 0; i < VboxLength; i++)
            {
                if (partialsum[i] != -1)
                {
                    lookaheadsum[i] = total - partialsum[i];
                }
            }

            // determine the cut planes
            return maxw == rw ? DoCut(0, vbox, partialsum, lookaheadsum, total) : maxw == gw
                    ? DoCut(1, vbox, partialsum, lookaheadsum, total) : DoCut(2, vbox, partialsum, lookaheadsum, total);
        }

        /// <summary>
        ///     Inner function to do the iteration.
        /// </summary>
        /// <param name="lh">The lh.</param>
        /// <param name="comparator">The comparator.</param>
        /// <param name="target">The target.</param>
        /// <param name="histo">The histo.</param>
        /// <exception cref="System.Exception">vbox1 not defined; shouldn't happen!</exception>
#if NETCOREAPP
        private static void Iter(List<VBox> lh, IComparer<VBox> comparator, int target, ReadOnlySpan<int> histo)
#else
        private static void Iter(List<VBox> lh, IComparer<VBox> comparator, int target, int[] histo)
#endif
        {
            int ncolors = 1;
            int niters = 0;

            while (niters < MaxIterations)
            {
                VBox vbox = lh[lh.Count - 1];

                if (vbox.Count(false) == 0)
                {
                    lh.Sort(comparator);
                    niters++;
                    continue;
                }

                lh.RemoveAt(lh.Count - 1);

                // do the cut
                VBoxesRef vboxesRef = MedianCutApply(histo, vbox);

                if (vboxesRef.VBox1.isDummy)
                {
                    throw new Exception(
                        "vbox1 not defined; shouldn't happen!");
                }

                lh.Add(vboxesRef.VBox1);

                if (!vboxesRef.VBox2.isDummy)
                {
                    lh.Add(vboxesRef.VBox2);
                    ncolors++;
                }
                lh.Sort(comparator);

                if (ncolors >= target)
                {
                    return;
                }
                if (niters++ > MaxIterations)
                {
                    return;
                }
            }
        }

        public static CMap Quantize(IEnumerable<int> pixelEnumerable, int maxcolors, bool ignoreWhite)
        {
            int[] histo = new int[Histosize];
            int rmin = 1000000, rmax = 0;
            int gmin = 1000000, gmax = 0;
            int bmin = 1000000, bmax = 0;

            int pixelLength = 0;

            foreach (int pixel in pixelEnumerable)
            {
                byte r = (byte)pixel;
                byte g = (byte)(pixel >> 8);
                byte b = (byte)(pixel >> 16);
                byte a = (byte)(pixel >> 24);

                if (a < 180) continue;

                if (!(ignoreWhite && LumaUtils.CalculateYiqLuma(r, g, b) > LumaUtils.IgnoreWhiteThreshold))
                {
                    int rval = r >> Rshift;
                    int gval = g >> Rshift;
                    int bval = b >> Rshift;

                    int index = GetColorIndex(rval, gval, bval);
                    histo[index]++;

                    if (rval < rmin) rmin = rval;
                    if (rval > rmax) rmax = rval;

                    if (gval < gmin) gmin = gval;
                    if (gval > gmax) gmax = gval;

                    if (bval < bmin) bmin = bval;
                    if (bval > bmax) bmax = bval;

                    pixelLength++;
                }
            }

            // get the beginning vbox from the colors
            List<VBox> pq = new List<VBox>
            {
                new VBox(rmin, rmax, gmin, gmax, bmin, bmax, histo) { count = pixelLength }
            };

            // Round up to have the same behaviour as in JavaScript
            int target = (int)Math.Ceiling(FractByPopulation * maxcolors);

            // first set of colors, sorted by population
            Iter(pq, ComparatorCount, target, histo);

            // Re-sort by the product of pixel occupancy times the size in color
            // space.
            pq.Sort(ComparatorProduct);

            // next set - generate the median cuts using the (npix * vol) sorting.
            Iter(pq, ComparatorProduct, maxcolors - pq.Count, histo);

            // Reverse to put the highest elements first into the color map
            pq.Reverse();

            // calculate the actual colors
            CMap cmap = new CMap();
            foreach (VBox vb in pq)
            {
                cmap.Push(vb);
            }

            return cmap;
        }
    }
}