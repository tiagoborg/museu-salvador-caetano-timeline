/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * ActivitySettings can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/

namespace EnigmaVirtual
{
    [System.Serializable]
    public class ActivitySettings
    {
        public bool isActive = false;
        public bool useMouse = true;
        public bool useKeyBoard = true;
        public int timeOut = 10;
    }
}

