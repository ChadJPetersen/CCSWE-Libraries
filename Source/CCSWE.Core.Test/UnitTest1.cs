using System;
using CCSWE.Collections.ObjectModel;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using System.Collections.Generic;

namespace CCSWE.Core.Test
{
    /// <summary>
    /// test class for <see cref="SynchronizedObservableCollection"/>
    /// </summary>
    [TestClass()]
    public class SynchronizedObservableCollectionTest 
    {

        /// <summary>
        /// test that the enumerator gets all the items added. even those after its original enumeration.
        /// </summary>
        [TestMethod]
        public void getEnumeratorTest()
        {
            //test object
            SynchronizedObservableCollection<int> testCol = new SynchronizedObservableCollection<int>();
            var r = (Enumerable.Range(0, 5)); 
            var z = (Enumerable.Range(5, 10));
            //add a range of numbers (0-4) to increment on
            foreach (int i in r) 
            {
                testCol.Add(i);
            }
            //the counter to use to make sure the thread is getting updates to
            //the enumerator while it is computing.
            int k = 0;
            Thread s = new Thread(new ThreadStart(() =>
                    {
                        //for-each item in the test wait a bit so the adder
                        //thread has a chance to add more items then add to the
                        //counter of how many items the enumerator got.
                        foreach (int i in testCol)
                        {
                            Thread.Sleep(10);
                            k++;
                        }
                    }));
            s.Start();
            //add more numbers during the execution of the other thread. adds
            //(5-14)
            foreach (int i in z)
            {
                testCol.Add(i);
            }
            s.Join();
            //assert that the number of items counted by the enumerator is equal
            //to that of the final count.
            Assert.Equals(k, testCol.Count);
        }

        /// <summary>
        /// test that the enumerator can handle arbitrary inserts into the collection and remain in a consistent state. 
        /// </summary>
        [TestMethod]
        public void enumeratorTestInternalEntry()
        {
            //test object
            SynchronizedObservableCollection<int> testCol = new SynchronizedObservableCollection<int>();
            //the items to add
            var r = (Enumerable.Range(0, 100));
            var z = (Enumerable.Range(100, 100));
            //add a range of numbers (0-4) to increment on
            foreach (int i in r)
            {
                testCol.Add(i);
            }
            //the list that will contain the enumerated items.
            List<int> k = new List<int>();
            Thread s = new Thread(new ThreadStart(() =>
            {
                //for-each item in the test wait a bit so the adder
                //thread has a chance to add more items then add to the
                //counter of how many items the enumerator got.
                foreach (int i in testCol)
                {
                    Thread.Sleep(10);
                    k.Add(i);
                }
            }));
            s.Start();
            //add more numbers during the execution of the other thread. adds
            //(5-14) at a random location.
            Random rnd = new Random();
            foreach (int i in z)
            {
                testCol.Insert(rnd.Next(0, testCol.Count-1),i);
            }
            s.Join();
            //assert that there are no duplicate items. (that the enumerator did
            //not back over something already enumerated) all items in testCol
            //are distinct in this test.
            Assert.AreEqual(k.Distinct().Count(), k.Count());
            //assert that the enumerator is not adding any items.
            Assert.IsTrue(k.Count() <= testCol.Count);
        }
    }
}
