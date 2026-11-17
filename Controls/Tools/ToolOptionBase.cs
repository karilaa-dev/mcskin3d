//
//    MCSkin3D, a 3d skin management studio for Minecraft
//    Copyright (C) 2013 Altered Softworks & MCSkin3D Team
//
//    This program is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.
//
//    This program is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MCSkin3D
{
	public class ToolOptionBase : UserControl
	{
		private FlowLayoutPanel _groups;

		internal void ArrangeOptions(int availableWidth)
		{
			if (_groups == null)
			{
				var controls = Controls.Cast<Control>().OrderBy(c => c.Left).ToArray();
				_groups = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
				foreach (var control in controls)
				{
					control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
					control.Margin = new Padding(3);
					_groups.Controls.Add(control);
				}
				Controls.Add(_groups);
			}
			int naturalWidth = _groups.Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal);
			Width = Math.Min(Math.Max(1, availableWidth), naturalWidth);
			Height = _groups.GetPreferredSize(new Size(Width, 0)).Height;
		}

		public ToolOptionBase()
		{
			SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
			DoubleBuffered = true;
		}

		public virtual void BoxShown()
		{
		}

		public virtual void BoxHidden()
		{
		}
	}
}
