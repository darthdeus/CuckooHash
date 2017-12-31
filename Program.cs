using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CuckooHash {
    // **********************************
    // RNG for UInt64 (ulong), taken from https://stackoverflow.com/a/13095144/72583 
    public static class RandomExtensionMethods {
        public static ulong NextUlong(this Random random, ulong min, ulong max) {
            if (max <= min)
                throw new ArgumentOutOfRangeException("max", "max must be > min!");

            ulong uRange = (ulong) (max - min);

            ulong ulongRand;
            do {
                byte[] buf = new byte[8];
                random.NextBytes(buf);
                ulongRand = (ulong) BitConverter.ToInt64(buf, 0);
            } while (ulongRand > ulong.MaxValue - ((ulong.MaxValue % uRange) + 1) % uRange);

            return (ulong) (ulongRand % uRange) + min;
        }

        public static ulong NextUlong(this Random random, ulong max) {
            return random.NextUlong(0, max);
        }

        public static ulong NextUlong(this Random random) {
            return random.NextUlong(ulong.MinValue, ulong.MaxValue);
        }
    }
    // *************************************

    class HashTest {
        private const ulong U = ulong.MaxValue;
        private const int u = 64;
        public int k = 20;
        private ulong m => 1uL << k;
        private const int c = 8;

        private int _numSubstrings => u / c;

        private ulong _n;
        private ulong _a1;
        private ulong _a2;
        private ulong[,] _lookupTable1;
        private ulong[,] _lookupTable2;

        private readonly Random _random = new Random(1234);

        private ulong _insertSwapCount;
        private ulong _insertCount;

        public HashFunctionType HashFunction;

        void ResetHashSeeds() {
            _a1 = _random.NextUlong(U);
            _a2 = _random.NextUlong(U);

            _lookupTable1 = GenerateTable();
            _lookupTable2 = GenerateTable();
        }

        bool CuckooInsertNorehash(ulong[] table, ref ulong value, bool countInsert) {
            int maxSwaps = 6 * (int) Math.Max(1,
                               Math.Ceiling(Math.Log(Math.Max(_n, 1)) / Math.Log(2)));

            ulong currentA = _a1;
            ulong[,] currentTable = _lookupTable1;

            for (int i = 0; i < maxSwaps; i++) {
                ulong hash;

                if (HashFunction == HashFunctionType.Multiplicative) {
                    hash = MultiplicativeHash(currentA, value);
                } else if (HashFunction == HashFunctionType.Table) {
                    hash = LookupTableHash(currentTable, value);
                } else {
                    throw new NotImplementedException();
                }

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


                ulong hashNew;
                if (HashFunction == HashFunctionType.Multiplicative) {
                    hashNew = MultiplicativeHash(currentA, value);
                } else if (HashFunction == HashFunctionType.Table) {
                    hashNew = LookupTableHash(currentTable, value);
                } else {
                    throw new NotImplementedException();
                }

                if (hashNew == hash) {
                    currentA = currentA == _a1 ? _a2 : _a1;
                    currentTable = currentTable == _lookupTable1 ? _lookupTable2 : _lookupTable1;
                }
            }

            return false;
        }

        public void CuckooInsert(ulong[] table, ulong value) {
            int max_rehash_count = 1000;

            for (int i = 0; i < max_rehash_count; i++) {
                if (CuckooInsertNorehash(table, ref value, true)) {
                    return;
                } else {
                    Console.WriteLine("Rehashing ...");
                    table = RehashTable(table);
                }
            }

            throw new InvalidOperationException();
        }


        ulong[] RehashTable(ulong[] oldTable) {
            while (true) {
                rehash_again:
                ResetHashSeeds();

                ulong[] newTable = new ulong[m];

                for (ulong i = 0; i < m; i++) {
                    if (oldTable[i] != 0) {
                        if (!CuckooInsertNorehash(newTable, ref oldTable[i], false)) {
                            goto rehash_again;
                        }
                    }
                }

                return newTable;
            }
        }

        public void LinearInsert(ulong[] table, ulong value) {
            ulong hash;
            if (HashFunction == HashFunctionType.Multiplicative) {
                hash = MultiplicativeHash(_a1, value);
            } else if (HashFunction == HashFunctionType.Table) {
                hash = LookupTableHash(_lookupTable1, value);
            } else if (HashFunction == HashFunctionType.Modulo) {
                hash = ModuloHash(value);
            } else {
                throw new NotImplementedException();
            }

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

        public ulong LookupTableHash(ulong[,] table, ulong value) {
            ulong cmask = (1 << c) - 1;

            ulong result = 0;

            for (int i = 0; i < _numSubstrings; i++) {
                //ulong currMask = cmask << (i * c);

                ulong tableIndex = (value >> (i * c)) & cmask;

                result ^= table[tableIndex, i];
            }

            return result;
        }

        private ulong[,] GenerateTable() {
            var result = new ulong[1 << _numSubstrings, c];

            for (int i = 0; i < result.GetLength(0); i++) {
                for (int j = 0; j < result.GetLength(1); j++) {
                    result[i, j] = _random.NextUlong(m);

                    Debug.Assert(result[i, j] < m);
                }
            }

            return result;
        }


        public ulong ModuloHash(ulong value) {
            return value % m;
        }

        public void GenericRun(Action<ulong[], ulong> inserter, float maxFill, string filename) {
            using (var writerTime = new StreamWriter($"time-{filename}"))
            using (var writerSwaps = new StreamWriter(filename)) {
                for (float fill = 0.01f; fill < maxFill; fill += 0.01f) {
                    ulong maxMembers = (ulong) Math.Floor(m * fill);

                    ResetHashSeeds();

                    ulong[] table = new ulong[m];

                    _insertSwapCount = 0;
                    _insertCount = 0;

                    _n = 0;

                    var stopwatch = new Stopwatch();
                    stopwatch.Reset();
                    stopwatch.Start();

                    for (ulong i = 0; i < maxMembers; i++) {
                        inserter(table, Math.Max(1, _random.NextUlong(U)));
                        _n++;
                    }

                    stopwatch.Stop();

                    float swapsPerIns = ((float) _insertSwapCount / (float) Math.Max(1uL, _insertCount));
                    float timePerIns = 1000 * stopwatch.ElapsedMilliseconds / (float) _n;

                    Console.WriteLine(
                        $"Fill({maxMembers}): {fill:.00}%" +
                        $"\tinserts: {_insertCount:00000000}" +
                        $"\tswaps/ins: {swapsPerIns:0.000}" +
                        $"\ttime/ins: {timePerIns:0.00000}ns");

                    writerSwaps.WriteLine($"{fill} {swapsPerIns}");
                    writerTime.WriteLine($"{fill} {timePerIns}");
                }
            }
        }

        public void SequenceTest(Action<ulong[], ulong> inserter, string filename) {
            using (var minWriter = new StreamWriter($"min-{filename}"))
            using (var maxWriter = new StreamWriter($"max-{filename}"))
            using (var meanWriter = new StreamWriter($"mean-{filename}"))
            using (var medianWriter = new StreamWriter($"median-{filename}"))
            using (var decilWriter = new StreamWriter($"decil-{filename}")) {
                for (k = 7; k < 22; k++) {
                    ulong startBound = (ulong) Math.Floor(m * 0.89f);
                    ulong stopBound = (ulong) Math.Floor(m * 0.91f);

                    var runs = new List<float>();
                    for (int run = 0; run < 100; run++) {
                        ResetHashSeeds();

                        ulong[] table = new ulong[m];

                        for (ulong i = 1; i < startBound; i++) {
                            inserter(table, i);
                        }

                        _insertSwapCount = 0;
                        _insertCount = 0;


                        for (ulong i = startBound; i < stopBound; i++) {
                            inserter(table, i);
                        }

                        float swapsPerIns = ((float) _insertSwapCount / (float) Math.Max(1uL, _insertCount));
                        runs.Add(swapsPerIns);
                    }

                    float min = runs.Min();
                    float max = runs.Max();
                    float mean = runs.Average();
                    runs.Sort();
                    float median = runs[runs.Count / 2];
                    float decil = runs[(int) (runs.Count * 0.9f)];

                    Console.WriteLine($"k: {k}" +
                                      $"\tmin: {min:0.000}" +
                                      $"\tmax: {max:0.000}" +
                                      $"\tmean: {mean:0.000}" +
                                      $"\tmedian: {median:0.000}" +
                                      $"\tdecil: {decil:0.000}");

                    minWriter.WriteLine($"{k} {min}");
                    maxWriter.WriteLine($"{k} {max}");
                    meanWriter.WriteLine($"{k} {mean}");
                    medianWriter.WriteLine($"{k} {median}");
                    decilWriter.WriteLine($"{k} {decil}");
                }
            }
        }
    }


    enum HashFunctionType {
        Multiplicative,
        Modulo,
        Table
    }

    class Program {
        static void Main(string[] args) {
            var test = new HashTest();

            test.HashFunction = HashFunctionType.Table;
            Console.WriteLine("\nSEQ TABLE");
            test.SequenceTest(test.LinearInsert, "seq-linear-table.txt");

            Console.WriteLine("\nSEQ MULTIPLICATIVE");
            test.HashFunction = HashFunctionType.Multiplicative;
            test.SequenceTest(test.LinearInsert, "seq-linear-multiplicative.txt");

            Console.WriteLine("\nRANDOM, k = 20");
            test.k = 20;

            test.HashFunction = HashFunctionType.Table;

            test.GenericRun(test.CuckooInsert, 0.491f, "cuckoo-table.txt");
            test.GenericRun(test.LinearInsert, 0.95f, "linear-table.txt");

            test.HashFunction = HashFunctionType.Modulo;

            test.GenericRun(test.LinearInsert, 0.95f, "linear-modulo.txt");

            test.HashFunction = HashFunctionType.Multiplicative;

            test.GenericRun(test.CuckooInsert, 0.491f, "cuckoo-multiply.txt");
            test.GenericRun(test.LinearInsert, 0.95f, "linear-multiply.txt");
        }
    }
}