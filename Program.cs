using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CuckooHash {
    class HashTest {
        private const ulong U = ulong.MaxValue;
        private const int u = 64;
        private const int k = 20;
        private const ulong m = 1 << k;
        private const ulong half = m / 2L;
        private const ulong c = 8;

        private ulong _n;
        private ulong _a1;
        private ulong _a2;

        private readonly Random _random = new Random();

        private ulong _insertSwapCount;
        private ulong _insertCount;

        void ResetHashSeeds() {
            _a1 = _random.NextUInt64(U);
            _a2 = _random.NextUInt64(U);
        }

        bool CuckooInsertNorehash(ulong[] table, ulong value, bool countInsert) {
            int max_swaps = 6 * (int) Math.Max(1,
                                Math.Ceiling(Math.Log(Math.Max(_n, 1)) / Math.Log(2)));

            ulong current_a = _a1;

            for (int i = 0; i < max_swaps; i++) {
                ulong hash = MultiplicativeHash(current_a, value);

                if (table[hash] == 0) {
                    if (countInsert) {
                        _insertCount++;
                    }

                    table[hash] = value;
                    return true;
                }

                _insertSwapCount++;

                ulong tmp = value;
                value = table[hash];
                table[hash] = tmp;

                ulong hashNew = MultiplicativeHash(current_a, value);
                if (hashNew == hash) {
                    current_a = current_a == _a1 ? _a2 : _a1;
                } //prohodis

                //current_a = current_a == _a1 ? _a2 : _a1;
            }

            return false;
        }

        public void CuckooInsert(ulong[] table, ulong value) {
            int max_rehash_count = 1000;

            for (int i = 0; i < max_rehash_count; i++) {
                if (CuckooInsertNorehash(table, value, true)) {
                    break;
                } else {
                    Console.WriteLine("Rehashing ...");
                    table = RehashTable(table);
                }
            }
        }


        ulong[] RehashTable(ulong[] oldTable) {
            while (true) {
                rehash_again:
                ResetHashSeeds();

                ulong[] newTable = new ulong[m];

                for (ulong i = 0; i < m; i++) {
                    if (oldTable[i] != 0) {
                        if (!CuckooInsertNorehash(newTable, oldTable[i], false)) {
                            goto rehash_again;
                        }
                    }
                }

                return newTable;
            }
        }

        public void LinearInsert(ulong[] table, ulong value) {
            ulong hash = MultiplicativeHash(_a1, value);

            _insertCount++;
            while (table[hash] != 0) {
                hash = (hash + 1) % m;
                _insertSwapCount++;
            }

            table[hash] = value;
        }


        public ulong MultiplicativeHash(ulong a, ulong value) {
            return ((a * value) % U) / (U / m);
        }

        public void GenericRun(Action<ulong[], ulong> inserter, string filename) {
            using (var writer = new StreamWriter(filename)) {
                for (float fill = 0.01f; fill < 0.95f; fill += 0.01f) {
                    ulong maxMembers = (ulong) Math.Floor(m * fill);

                    ResetHashSeeds();

                    ulong[] table = new ulong[m];

                    ResetInsertCounts();

                    _n = 0;

                    for (ulong i = 0; i < maxMembers; i++) {
                        inserter(table, Math.Max(1, _random.NextUInt64(U)));
                        _n++;
                    }

                    float swapsPerIns = ((float) _insertSwapCount / (float) Math.Max(1uL, _insertCount));
                    Console.WriteLine(
                        $"Fill({maxMembers}): {fill:.00}%" +
                        $"\tinserts: {_insertCount:00000000}" +
                        $"\tswaps/ins: {swapsPerIns:0.000}");

                    writer.WriteLine($"{fill};{swapsPerIns}");
                }
            }
        }

        private void ResetInsertCounts() {
            _insertSwapCount = 0;
            _insertCount = 0;
        }
    }

    static class RndExtensions {
        public static UInt64 NextUInt64(this Random rnd, ulong max) {
            int rawsize = System.Runtime.InteropServices.Marshal.SizeOf(max);
            var buffer = new byte[rawsize];
            rnd.NextBytes(buffer);
            return BitConverter.ToUInt64(buffer, 0);
        }
    }

    class Program {
        static void Main(string[] args) {
            var test = new HashTest();
            test.GenericRun(test.CuckooInsert, "cuckoo-multiply.txt");
            test.GenericRun(test.LinearInsert, "linear-multiply.txt");
        }
    }
}