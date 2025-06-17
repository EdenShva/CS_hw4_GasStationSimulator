using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GasStationSimulator
{
    internal class Vehicle
    {
        public CancellationToken CancellationToken;
        private static int id = 1;
        private static object idLock = new();
        private int _idValue;

        public int Id
        {
            get
            {
                lock (idLock)
                {
                    return _idValue;
                }
            }
        }
        public Vehicle(CancellationToken token)
        {
            lock (idLock)
            {
                _idValue = id++;
            }
            CancellationToken = token;
        }
    }
}
