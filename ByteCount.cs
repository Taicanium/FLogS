using System;

namespace FLogS
{
	struct ByteCount
	{
		public ByteCount() { bytes = 0.0; prefix = -1; }
		private ByteCount(double b, short p) { bytes = b; prefix = p; }

		public double bytes;
		public short prefix;
		private readonly static string[] prefixes = ["k", "M", "G", "T", "P", "E", "Z", "Y", "R", "Q"]; // Always futureproof...

		public void Adjust(int factor, bool absolute = true)
		{
			if (!absolute)
				factor = (short)(prefix - factor);
			while (prefix != factor)
				Magnitude(Math.Sign(prefix - factor));
		}

		public void Magnitude(int factor)
		{
			bytes *= Math.Pow(1024, factor);
			prefix -= (short)factor;
		}

		public void Simplify()
		{
			while (bytes >= 921.6 && prefix < prefixes.Length)
				Magnitude(-1);
			while (bytes < 0.9 && prefix > -1)
				Magnitude(1);
		}

		public static ByteCount operator -(ByteCount a, ByteCount b)
		{
			ByteCount c, d;

			if (Math.Abs(a.prefix - b.prefix) > 3) // Special accommodations must be made if the disparity in magnitude would normally result in an int overflow.
			{
				c = new(a.bytes, a.prefix);
				d = new(b.bytes, b.prefix);
				c.Adjust(Math.Abs(a.prefix - b.prefix) / 2);
				d.Adjust(c.prefix);
				c.bytes -= d.bytes;
				c.Simplify();
				return c;
			}

			c = new(a.bytes, a.prefix);
			c.Adjust(b.prefix);
			c.bytes -= b.bytes;
			c.Simplify();
			return c;
		}

		public static ByteCount operator +(ByteCount a, ByteCount b)
		{
			ByteCount c, d;

			if (Math.Abs(a.prefix - b.prefix) > 3)
			{
				c = new(a.bytes, a.prefix);
				d = new(b.bytes, b.prefix);
				c.Adjust(Math.Abs(a.prefix - b.prefix) / 2);
				d.Adjust(c.prefix);
				c.bytes += d.bytes;
				c.Simplify();
				return c;
			}

			c = new(a.bytes, a.prefix);
			c.Adjust(b.prefix);
			c.bytes += b.bytes;
			c.Simplify();
			return c;
		}

		public static ByteCount operator +(ByteCount a, int b)
		{
			ByteCount c = new(a.bytes, a.prefix);
			ByteCount d = new(b, -1);
			c.Adjust((c.prefix + 1) / 2);
			d.Adjust(c.prefix);
			c.bytes += d.bytes;
			c.Simplify();
			return c;
		}

		public static ByteCount operator +(ByteCount a, uint b)
		{
			ByteCount c = new(a.bytes, a.prefix);
			ByteCount d = new(b, -1);
			c.Adjust((c.prefix + 1) / 2);
			d.Adjust(c.prefix);
			c.bytes += d.bytes;
			c.Simplify();
			return c;
		}

		public static ByteCount operator +(ByteCount a, long b)
		{
			ByteCount c = new(a.bytes, a.prefix);
			ByteCount d = new(b, -1);
			c.Adjust((c.prefix + 1) / 2);
			d.Adjust(c.prefix);
			c.bytes += d.bytes;
			c.Simplify();
			return c;
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
			return $"{bytes:N1} {prefixes[prefix]}B";
		}
	}
}
