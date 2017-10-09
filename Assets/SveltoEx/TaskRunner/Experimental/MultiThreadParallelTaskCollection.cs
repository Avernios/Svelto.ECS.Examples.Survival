//this was a good excercise, but with my current knowledge, I say that heavy parallelism
//is useless for generic game features.
using System;
using System.Collections;
using Svelto.DataStructures;

namespace Svelto.Tasks
{
    /// <summary>
    /// a ParallelTaskCollection ran by MultiThreadRunner will run the tasks in a single thread
    /// MultiThreadParallelTaskCollection enables parallel tasks to run on different threads
    /// </summary>
    public class MultiThreadParallelTaskCollection : IEnumerator
    {
        public event Action onComplete;

        const int MAX_CONCURRENT_TASKS = 1024;

        public MultiThreadParallelTaskCollection(uint numberOfThreads = MAX_CONCURRENT_TASKS)
        {
            _runners = new MultiThreadRunner[numberOfThreads];
            _taskRoutines = new ITaskRoutine[numberOfThreads];
            _parallelTasks = new ParallelTaskCollection[numberOfThreads];

            //prepare a single multithread runner for each group of fiber like task collections
            //number of threads can be less than the number of tasks to run
            for (int i = 0; i < numberOfThreads; i++) _runners[i] = new MultiThreadRunner();

            //prepare the fiber-like paralleltasks
            for (int i = 0; i < numberOfThreads; i++)
            {
                var ptask = TaskRunner.Instance.AllocateNewTaskRoutine();
                var ptc = new ParallelTaskCollection();
                ptc.onComplete += DecrementConcurrentOperationsCounter;

                ptask.SetEnumerator(ptc).SetScheduler(_runners[i]);

                _parallelTasks[i] = ptc;
                _taskRoutines[i] = ptask;
                //once they are all done, the process will be completed               
            }

            _enumeratorCopy = new FasterList<IEnumerator>();
        }

        bool RunMultiThreadParallelTasks()
        {
            if (isRunning == false)
            {
                int concurrentOperations = _enumeratorCopy.Count;

                if (concurrentOperations > 0)
                {
                    //spread the tasks over the available parallel collections
                    for (int i = 0; i < concurrentOperations; i++)
                    {
                        //in case tasks are added before a clear and after a run:
                        var index = _numberOfConcurrentOperationsToRun + i;
                        var yieldIT = _enumeratorCopy[index];

                        ParallelTaskCollection parallelTaskCollection = _parallelTasks[index % _parallelTasks.Length];
                        parallelTaskCollection.Add(yieldIT);
                    }

                    concurrentOperations += _numberOfConcurrentOperationsToRun;

                    //decide how many threads to run
                    _numberOfConcurrentOperationsToRun = _counter = Math.Min(_parallelTasks.Length, concurrentOperations);

                    _enumeratorCopy.Clear();
                }
                //start them
                for (int i = 0; i < _numberOfConcurrentOperationsToRun; i++)
                    _taskRoutines[i].ThreadSafeStart();
            }

            MultiThreadRunner.MemoryBarrier();
            //wait for completition, I am not using signaling as this Collection could be yield by a main thread runner
            isRunning = _counter > 0;

            return isRunning;
        }

       
        public void Add(IEnumerator enumerator)
        {
            if (isRunning == true)
                throw new Exception("can't add enumerators on a started MultiThreadedParallelTaskCollection");

            _enumeratorCopy.Add(enumerator);
        }

        public bool MoveNext()
        {
            if (RunMultiThreadParallelTasks()) return true;

            if (onComplete != null)
                onComplete();

            Reset();

            return false;
        }

        public void Reset()
        {
            _counter = _numberOfConcurrentOperationsToRun;
        }

        public void Clear()
        {}

        public object Current
        {
            get
            {
                return null;
            }
        }

        public bool              isRunning       { protected set; get; }

        void DecrementConcurrentOperationsCounter()
        {
            System.Threading.Interlocked.Decrement(ref _counter);
        }   

        MultiThreadRunner[]         _runners;
        int                         _counter;
        ParallelTaskCollection[]    _parallelTasks;
        ITaskRoutine[]              _taskRoutines;
        FasterList<IEnumerator> _enumeratorCopy;
        private int _numberOfConcurrentOperationsToRun;
    }
}
