using System;
using System.Collections.Generic;
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

        private ulong g_a1;
        private ulong g_a2;

        private Random random = new Random();

        private ulong insert_swap_count;
        private ulong insert_count;

        void ResetHashSeeds() {
            g_a1 = random.NextUInt64(U);
            g_a2 = random.NextUInt64(U);
        }

        bool CuckooInsertNorehash(ulong[] table, ulong value) {
            // TODO: FUUUUUUUUUUUUUUJ
            int max_swaps = 10;


            ulong current_a = g_a1;

            for (int i = 0; i < max_swaps; i++) {
                ulong hash = MultiplicativeHash(current_a, value);

                if (table[hash] == 0) {
                    insert_count++;

                    table[hash] = value;
                    return true;
                }

                insert_swap_count++;

                ulong tmp = value;
                value = table[hash];
                table[hash] = tmp;

                current_a = current_a == g_a1 ? g_a2 : g_a1;
            }

            return false;
        }

        void CuckooInsert(ulong[] table, ulong value) {
            int max_rehash_count = 1000;

            for (int i = 0; i < max_rehash_count; i++) {
                if (CuckooInsertNorehash(table, value)) {
                    break;
                } else {
                    Console.WriteLine("Rehashing ... \n");
                    table = RehashTable(table);
                }
            }
        }

        ulong[] RehashTable(ulong[] old_table) {
            while (true) {
                rehash_again:
                ResetHashSeeds();

                ulong[] new_table = new ulong[m];

                for (ulong i = 0; i < m; i++) {
                    if (old_table[i] != 0) {
                        if (!CuckooInsertNorehash(new_table, old_table[i])) {
                            goto rehash_again;
                        }
                    }
                }

                return new_table;
            }
        }

        public ulong MultiplicativeHash(ulong a, ulong value) {
            return ((a * value) % U) / (U / m);
        }

        public void Cuckoo() {
            for (float fill = 0.01f; fill < 0.95f; fill += 0.01f) {
                ulong maxMembers = (ulong) Math.Floor(m * fill);

                ResetHashSeeds();

                ulong[] table = new ulong[m];

                ResetInsertCounts();

                for (ulong i = 0; i < maxMembers; i++) {
                    CuckooInsert(table, Math.Max(1, random.NextUInt64(U)));
                }

                float swapsPerIns = ((float) insert_swap_count / Math.Max(1uL, insert_count));
                Console.WriteLine(
                    $"Fill({maxMembers}): {fill:.00}%" +
                    $"\tinserts: {insert_count}" +
                    $"\tswaps/ins: {swapsPerIns:.2}\n");
            }
        }

        private void ResetInsertCounts() {
            insert_swap_count = 0;
            insert_count = 0;
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
            new HashTest().Cuckoo();
        }
    }
}