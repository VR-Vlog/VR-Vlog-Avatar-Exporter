using System;
using System.Linq;
using UnityEngine;

namespace VRVlog.LilToonExporter.LanTransfer
{
    // What the QR area shows until "QRを表示" is pressed: three blurred finder squares and a few
    // blocks on white (132px PNG, grey). A fixed picture, not made from any link, so nothing on
    // screen can be read before the user shows the QR. Embedded because the package ships
    // source files only, not image assets.
    internal static class QrCover
    {
        private const string TextureName = "VRVlog.QrCover";
        private static Texture2D texture;

        // Not an asset, so it outlives script reloads; pick the existing one up again.
        internal static Texture2D Texture => texture != null ? texture : texture =
            Resources.FindObjectsOfTypeAll<Texture2D>().FirstOrDefault(item => item.name == TextureName) ?? Create();

        private static Texture2D Create()
        {
            var created = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                { name = TextureName, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            created.LoadImage(Convert.FromBase64String(Png), true);
            return created;
        }

        private const string Png =
            "iVBORw0KGgoAAAANSUhEUgAAAIQAAACECAAAAAB0L9x7AAAO+ElEQVR42u1b23bbSJKMvFQVQMnunv//xTnTblkigLpk7kMBFElBImnPnt2H5ousYyGYzMrK" +
            "S2SQHP/3L8Y/RvxjxD9G/D82Qq9+94sfpxdd/Ljj9RCOXj3pDv/4LEAAge6041GccyPcHebu7jsPExERrwg3bHgUR8/MdzezZmYfnyYiZhZmvumMX8C58IS1" +
            "WmutzXYeZlFVVReQXx/xtRl34IB3PQG3WsuSl1KbfXiYRUOKyZXYyeH4PEbuwnEl+miEu1nL8zRNS6728SprTONoDnJCd/N+jNyJQ0x8evTSE3l6+/k2LcWA" +
            "M0PdAQ5pzA1EJHAzdxCtZ0sfPXEDx8AivBuY3soyvb68HOfiIDo97e4OCsOhQUTY0Kw2B4mqCpicrlxxE4dYVcVpNyZant5e/n6bqzMzEchBa7CTDgUSgzJ7" +
            "qbkaWGOMAdc23IOjIQ3n4XIRE7Usx7e316URK5/C182quVToMA5RyPIylwYJQwMR06UZ9+GUaua+GxOt5GWZl2xC3m98Dzz31hp0WHKpheo8TUuFpgZmFr52" +
            "xV04bdcT2/0utTmxqBDMARCDyc281VJqbQ0lz8e5QhtpCMHdP0TFLZx6dXnPA9PdzcyJOURlMu93gJ0JMPKeBtFqLSXDpNZmZkZ2kS/uwbHLnH5VO9yJmSQE" +
            "hXtrBhYmFnI0Yup/cP73Zo3ARHR+U+/D+aKUAyzQoGytLLVCFaSsDkJPLkQiGhpBgzK8NThTjz66F+dWP0HMAmFYXaYpV2hsDgWLg7nXHyRzqZAUA3vLwizC" +
            "wnQ/Dn1tBKHnYm/L8e11KQipOSAgBhGBWFSJJTUXDYGsVGKRoBep8RbOl03NWu8Br2V+e3mZCsJoYAoE6q0As5KEUM2ZmdCqOUtoaX3TO3FuHMeaeC1Pbz9f" +
            "jhmxkKgQowckkai4xea+lsvaSJMRCz+Cc9sIGGpZ5uPbW0b0MAxJ9ORlFiUP7gZr2cu8VNIGUZGP8J/i3DbCHbDWSsk5A6GU9p5aiIiVyB3mLZOVeSqI4BCD" +
            "P4Jzhye+eBExM8HhBkYrec4wCbW573r6l1r+9bqzSAixADGGIMLvyaj3qYCBqEcFcW/krm34GuemJxga0nAolBEPhyEFZdi5N/zU0rr7p+39LZwvjSBi9rE2" +
            "DwVh/PY0Ru314WKuMHNzEAuEmfdGgZs4n05g7g6QkBt4LAjp+WmIYv1jn5p6WLNam4NZKQQVostsdQ/OZ0Y43A0MUoAkV2gcx6SA2+p1wB1mVmvJpRoEHILK" +
            "Vdq+B+cLT5g1EAsrSGqFaorKZq3B+gVzuLdWc8nLvJQGFlWV6zR4G+fLmLDmDlJIgBpYgsBqKY3s9CmtlrwsyzwvuRgTM38sSrdx9o0gIuqtC8iZSMjATG5W" +
            "SzExXwuCt5qnaZrmJRcj1etqcBfO/kC81icmNwNcCA4C3MxbKRVMzExEcLSyzG/H47SUBnE1P7/99+LsTWDrjKZBpZk1cu7ZwM2tNXMWDarCRHBrZZmPb9NS" +
            "jAM3c7ifgd6Hs38cxBJiSkNF64j0niWFJQ0pxSC8Tnq15JyrM/c2lh7G2RuIiZg1pMNcoR+GFjXS4enpkILy6f0IRMQi0v/4UZyzZy49EcdSEeZitDe+fX8a" +
            "ozATEbNoiI0UElOKQeTMFXfi0H5gSki1OQ9zbf1zXg2yT89jCkIgZo1Dg/Q5bBxSkDMHXeKsMNQT7TkO7ceERgOHZanN+0n203TaRvr+CYhYQ3KJ1YhDSuMQ" +
            "9dyKcxzHFhW0/V8axzHqfkyAXSJIYs61WZ+nefPHSm7EoAR3J5LoHMxJNMYt0OgDTnV7v7VrDGlIMQX5PCYAEg2xVnPv9AP33nijeYTh1lozhzg7sYQQQwwX" +
            "H+wMp61MRuepiHijiz6JCaxtPRERNVv5B1nHK1qTM3lrJZfuLSIJIcYQpHdbJ59uOABszRuqIswdRq7SvJ43QgyIGROsNTCBNajy6oo1Z1sty7wsuTaHqLNu" +
            "DZ/jdPjEHcetleYgcV6xGLxSiPSZJ0BE5N7KkhskkoBlddzal5nVMh/fpmnJ5qxpMNLQW4SNxyIiwoazLJ1OsRMWrfeOPq2ivW0s87QUhAaJTizvc6Y7vC3H" +
            "nz9/vh1zc0njU4OotN5onfFY3WXzcVqygWMy2rDoFq288ix5meaCYByq+QUj7d5Knn7+9ePlda7Q4fl7I1URv+Sxer2qeZmOU26Q2CCxmmNn+trtJ3pdKBmQ" +
            "2jqR8G6Cu9Xl+Prj33/10e57hsYY5IrHkpU1qCXnpUEgoTYzf6Dl/9Bz+nrXAZi1PL/+/de/f7wVhKeMMB7GdM1jEf/m3NHrQucfhIlgjvWuE9yt1mV6ffnx" +
            "n9eMuCAepqUUueax3ImYJcRi1MApBpWd5uszaoBYQqouFZqGpEJubu4gFu5eriUvx+Prz4yI4bgs5ZrHiua+4hhpaZCQxiGe15cvjSBiCWaQ1CBhGKKQeet0" +
            "T9BOwPnaS2Qg59JpK1jnsXyNIyKSYOYchmpgDWlIeqcnyInFHRJ6kIWgsFpKMXBoCUJ375M7Tsy1GYhVe2Z3v2cMJBIQa+zXTQTeyrwsDZKMmHitIiHGCMTY" +
            "z5oZrO9xxERE6Din4iFCbq0TVnTjOJwYxBK2ULTayjxNFdogygRiDTEdDs/IiM+HQ0pBRQitbXEUhImIHP1ofZtXDXAI+A5PAMwmawqGO1pZpmPuHIQyWDWN" +
            "z98zUkF4+vP785hCCCTEWxytVqw4ndNs1QwkYuGK2/qEswLhtFRxN7eal3npHISBSOLw/EdB7MnqX388jSkEdtawJau1Qqw47taKt1KMJJhfc1uf5ImzQ7NO" +
            "R9dSwZ0JJtF0WAri05q2//x2SCEwxOIpbQudGhzADc1rnnOFJiPZ4bb2qYFTa+Dbib6nshDHBh2/5wZO49PztzEFZVIL7wXsYvtJ8PO42uO29Ku9ppmZda5s" +
            "bW+YPDk4PS/FwBqHwzgGZSHgbCV1eexu53EV/D5PnPaaZrUaWAJRTLHfPongMJTS73+IPQHQyt587BW8z66nuNrjtnbZu22vaX2M1qQcT3ePSMJQm1nPGSJw" +
            "I6cTT0t7e9L3uNrj13RvuWqt1Vpra9ZaLcYhSux9PYNAomZmvrIxBjhATkT477F37mtxqNXMzJ1UQxpSVCYiv7z/7XT/5TPScU2yhrC1d7ebGri1mqdpXtbE" +
            "LxpCSikFZcZD939NwiQaU2PEFFX2rNg5jnXyP05zaU4SIkXpAcgP3v/3iWwwROg4RGW68zhW/mFeGjgkBGcJ4X1eOb//x7kgVGdZ7/+ZboLWf5FIbJAGSUN3" +
            "xX15wqyVnJelubiYdwKA+MP9z/PxmBGNQ1olAufE6nZZSc05GDjEKEwP95id+ukcBNPH/ec8vWUU0qGaCa50E9vAxBJJYm+MlHf6K90/xbU/rJCYYuw85c4m" +
            "uOZ5ymiaajPv/Oa7buJ9dGSQ2toi3umJq/5wGMYLgubSilIWUOk2GDbdxLoJ3YZoEt7WkvfNHVufeeoPYxouWZCz4mZmraF/eLhbzXlZSqlmAJg1hJRiCEKM" +
            "1Td77cROKd/6zJjXmSquPcItGYt7y/PxOM1Lbt35MQ3j4QCi7Ursbxr2S7kAJGFYobpPaT9mWTaWH+Z1Ob6+dOGEgTmk8embgUTxSV35UmfFSqLrermH03rl" +
            "P2pTgmPNIW4lTy9//Xh5nUp1kIbx+XuFhKDytRpoT2fl7mDQKcaJ4TC61tAQi8ahCuIQVRjetjn12AmBdPieEVJK5vcXsLWPsPdtDgB3gnW66UJD0/nKoXpA" +
            "HIegBPg2p77OtUF0eM4I41Nu51qJr40410fZ1UaCOiN9qY8iljgYF4RhiCLkVssyvf794z8v63CcEQ7TUupDnnhEZ+UAa2ykDZKGFJi2mvP682UqDRIyxad5" +
            "Ke2WDb+hs3InDs6h0VoTzN1qzcs8TZ0aQZyXXKvt6t92jXhUZ+VwJ4U07zWB1sLXai055waBlFpbM/PHPPGAzsqJQKTiAImwbFfLrDVrrQHNWutd4P3t3cM6" +
            "K175fSJiJv/l/fDv6axCiJ2Q2+QsRKAu6RBAhEWY96vFFzHxoM4qDSAm5u1teuuhGmJDg8QYNhr3IU88qLMiUV0XO9QzvGpMw5ihDRLGcUhRlenWNPCbOqto" +
            "6zDTA4c1Dofn7LEnq2/Ph94FPBCYj+ms/J3n3OZWYk2Hb9nDUgyiw/Offzz3juih2vGQzmqbGTc6rRnr8JwRn/NWwP71x/MQb4bFb+mshInc4LbOrdUlPZse" +
            "5tqMein/83lMD3niMZ3VynOieWvr3FoKxQMPS23mW1Pz7RCVz4aAe8jUu3VW7zxnLWWbW1uTMRx6oiaWGIfDYYxCbl/qvn9ZZ7XynOQ1L6e5lYjjsOUuZtUY" +
            "YxQyt3WLuq/7/mWdVec5CXXtbkvrWzlJoacGEJOICrP1e/y57vuXdVYr5d681dPcqomUwxjjxt8xEWDWmhmRfNBr/7bOqtdZN/ezuVUNonFIfVaitUnJuRSD" +
            "aEzxUq/92zqrvvQ1utQssEgIIYRuhMOstWVa79k4HnCh1/5tnRUAOJ3tNda5NYZOZW6ropann6+vc0EYnhvdz2Pe1lmdTm53bu0c+6Z8ydPrj5e3BempQjTo" +
            "nTzm3fqo23OrW8vz28tfP2cMGWEYBvP/js7q7rnV13bt7effMxbEw1Lu4TEf00e970fqOrfqxm2/s3DWal6WGbTkThrcPo6H9FHv+xG7XMyDfpPHfEAfdbYf" +
            "eV9Sn3PbvefUmJIjrcT0DR7zYX3U5X7kI7dNTiQhjU8ZCcO3p1VA8sVx/II+6no/8rFcE0scngrigvT0/Wm4zd49rI/CrW+HEUmIhwY9FITh+dshhhs85uP6" +
            "qDuyPGtyl7Tes0+6LP0dfdRNE7q+hsNQWy9g+/3mvs5qfwz8oI+6yxNEEk6yBr0RE10fZS7DZwPxB33UXVb0/QiAj1qaz3VW8fA5NTBGfcgThIs968f92K7O" +
            "ijikr0iSII/ExPWelW7zmMRKxCF9SRc9GBNXeeSOln87vy+/WPgLLMTNR87FCHd9xfLX1233GXH7y6YE/C/YgGtZxn/r67u/ZQT++Ur3P0b8Y8Q/Rly9/gdT" +
            "MAYawQUKZAAAAABJRU5ErkJggg==";
    }
}
