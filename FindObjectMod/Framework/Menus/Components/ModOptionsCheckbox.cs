﻿using StardewValley.Menus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindObjectMod.Framework.Menus.Components
{
    public class ModOptionsCheckbox : OptionsCheckbox
    {
        public Action<bool> actionBool { get; }

        public ModOptionsCheckbox(string label, int whichOption, Action<bool> ActionBool, bool Value, int x = -1, int y = -1) : base(label, 9999, x, y)
        {
            this.actionBool = ActionBool;
            this.isChecked = Value;
        }

        public override void receiveLeftClick(int x, int y)
        {
            bool old = this.isChecked;
            base.receiveLeftClick(x, y);
            bool flag = this.isChecked != old;
            if (flag)
            {
                Action<bool> actionBool = this.actionBool;
                if (actionBool != null)
                {
                    actionBool(this.isChecked);
                }
            }
        }
    }
}
