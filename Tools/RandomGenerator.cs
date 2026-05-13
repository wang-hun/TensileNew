using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TensileNeW.Tools
{
    /// <summary>
    /// 多线程
    /// </summary>
    public class ThreadSafeRandomGenerator
    {
        private static readonly ThreadLocal<Random> threadLocalRandom =
            new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

        public static Random Instance => threadLocalRandom.Value;

        public static int[] GenerateConcurrentRandomArray(int length, int minValue = 0, int maxValue = 100)
        {
            int[] array = new int[length];
            Parallel.For(0, length, i =>
            {
                array[i] =  Instance.Next(minValue, maxValue);
            });
            return array;
        }
    }
 

    /// <summary>
    /// 单线程
    /// </summary>
    public class RandomGenerator
    {
        // 使用静态Random实例确保随机数生成的多样性
        private static Random random = new Random();

        /// <summary>
        /// 生成一个指定长度的随机整数数组
        /// </summary>
        /// <param name="length">数组长度</param>
        /// <param name="minValue">随机数最小值（包含）</param>
        /// <param name="maxValue">随机数最大值（不包含）</param>
        /// <returns>随机整数数组</returns>
        public static int[] GenerateRandomIntArray(int length, int minValue = 0, int maxValue = 100)
        {
            if (length <= 0)
                throw new ArgumentException("数组长度必须大于0。");

            int[] array = new int[length];
            for (int i = 0; i < length; i++)
            {
                array[i] = random.Next(minValue, maxValue);
            }
            return array;
        }

        /// <summary>
        /// 生成一个指定长度的随机双精度浮点数数组（0.0到1.0之间）
        /// </summary>
        /// <param name="length">数组长度</param>
        /// <returns>随机双精度浮点数数组</returns>
        public static float[] GenerateRandomDoubleArray(int length, double minValue = 0d, double maxValue = 100d)
        {
            if (length <= 0)
                throw new ArgumentException("数组长度必须大于0。");
            if(maxValue<minValue)
            {
                var tempMin = minValue;
                var tempMax = maxValue;
                maxValue = tempMin;
                minValue = tempMax;
            }

            float[] array = new float[length];
            for (int i = 0; i < length; i++)
            {
                array[i] =(float) (random.NextDouble() * (maxValue - minValue) + minValue);
            }
            return array;
        }

        public static bool[] GenerateRandomBoolArray(int length)
        {
            if (length <= 0)
                throw new ArgumentException("数组长度必须大于0。");

            bool[] array = new bool[length];
            for (int i = 0; i < length; i++)
            {
                var v= random.Next(0, 10);
                if (v > 5)
                    array[i] = true;
                else
                    array[i] = false;
            }
            return array;
        }

    }


}

