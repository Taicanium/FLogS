using System;

namespace FLogS
{
    struct ByteCount
    {
        public ByteCount() { bytes = 0.0; prefix = -1; }
        public ByteCount(double b, short p) { bytes = b; prefix = p; }

        public double bytes;
        public short prefix;

        public void Adjust(int factor, bool absolute = true)
        {
            if (!absolute)
                factor = (short)(prefix - factor);
            while (prefix < factor)
                Magnitude(-1);
            while (prefix > factor)
                Magnitude(1);
            return;
        }

        public void Magnitude(int factor)
        {
            bytes *= Math.Pow(1024, factor);
            prefix -= (short)factor;
        }

        public void Simplify()
        {
            while (bytes >= 921.6 && prefix < Common.prefixes.Length)
                Magnitude(-1);
            while (bytes < 0.9 && prefix > -1)
                Magnitude(1);
            return;
        }

        public static ByteCount operator -(ByteCount a, ByteCount b)
        {
            ByteCount o;
            ByteCount o2;

            if (Math.Abs(a.prefix - b.prefix) > 3) // Special accommodations must be made if the disparity in magnitude would normally result in an int overflow.
            {
                o = new(a.bytes, a.prefix);
                o2 = new(b.bytes, b.prefix);
                o.Adjust(Math.Abs(a.prefix - b.prefix) / 2);
                o2.Adjust(o.prefix);
                o.bytes -= o2.bytes;
                o.Simplify();
                return o;
            }

            o = new(a.bytes, a.prefix);
            o.Adjust(b.prefix);
            o.bytes -= b.bytes;
            o.Simplify();
            return o;
        }

        public static ByteCount operator +(ByteCount a, ByteCount b)
        {
            ByteCount o;
            ByteCount o2;

            if (Math.Abs(a.prefix - b.prefix) > 3)
            {
                o = new(a.bytes, a.prefix);
                o2 = new(b.bytes, b.prefix);
                o.Adjust(Math.Abs(a.prefix - b.prefix) / 2);
                o2.Adjust(o.prefix);
                o.bytes += o2.bytes;
                o.Simplify();
                return o;
            }

            o = new(a.bytes, a.prefix);
            o.Adjust(b.prefix);
            o.bytes += b.bytes;
            o.Simplify();
            return o;
        }

        public static ByteCount operator +(ByteCount a, int b)
        {
            ByteCount o = new(a.bytes, a.prefix);
            ByteCount o2 = new(b, -1);
            o.Adjust((o.prefix + 1) / 2);
            o2.Adjust(o.prefix);
            o.bytes += o2.bytes;
            o.Simplify();
            return o;
        }

        public static ByteCount operator +(ByteCount a, uint b)
        {
            ByteCount o = new(a.bytes, a.prefix);
            ByteCount o2 = new(b, -1);
            o.Adjust((o.prefix + 1) / 2);
            o2.Adjust(o.prefix);
            o.bytes += o2.bytes;
            o.Simplify();
            return o;
        }

        public static ByteCount operator +(ByteCount a, long b)
        {
            ByteCount o = new(a.bytes, a.prefix);
            ByteCount o2 = new(b, -1);
            o.Adjust((o.prefix + 1) / 2);
            o2.Adjust(o.prefix);
            o.bytes += o2.bytes;
            o.Simplify();
            return o;
        }

        /// <summary>
        /// Return the size of this byte counter, formatted with the most appropriate metric prefix. This function does not preserve manual adjustments to the counter's magnitude.
        /// </summary>
        /// <returns>The byte counter size, in the format "0.0 xB", where 'x' is a metric prefix.</returns>
        public override string ToString()
        {
            Simplify();
            if (prefix == -1)
                return $"{bytes:N0} B";
            return $"{bytes:N1} {Common.prefixes[prefix]}B";
        }
    }
}
