using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MemeMachine
{
    public class MemeSound
    {
        public string Name { get; set; }
        public TimeSpan Length { get; set; }
        public string Path { get; set; }
        public bool isSelected = false;
        public bool isPlaying = false;
    }
}
