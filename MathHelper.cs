using System;

namespace DngOpcodesEditor;

public static class MathHelper
{
    // Convert a row-major 1d array to a 2d array
    public static float[,] ArrayToArray2D(float[] array, int width)
    {
        int height = array.Length / width;
        var newArray = new float[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                newArray[x, y] = array[x + y * width];
            }
        }
        return newArray;
    }
    public static double BilinearInterpolation(float[,] array, double x, double y)
    {
        int x1 = (int)x;
        int y1 = (int)y;
        int x2 = Math.Min(array.GetLength(0) - 1, x1 + 1);
        int y2 = Math.Min(array.GetLength(1) - 1, y1 + 1);
        if (x2 == x1)
            x1--;
        if (y2 == y1)
            y1--;
        float q11 = array[x1, y1];
        float q21 = array[x2, y1];
        float q12 = array[x1, y2];
        float q22 = array[x2, y2];
        double wd = (x2 - x1) * (y2 - y1);
        double w11 = (x2 - x) * (y2 - y) / wd;
        double w12 = (x2 - x) * (y - y1) / wd;
        double w21 = (x - x1) * (y2 - y) / wd;
        double w22 = (x - x1) * (y - y1) / wd;
        double result = w11 * q11 + w12 * q12 + w21 * q21 + w22 * q22;
        return result;
    }
}