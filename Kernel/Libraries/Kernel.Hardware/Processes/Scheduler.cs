﻿#region LICENSE
// ---------------------------------- LICENSE ---------------------------------- //
//
//    Fling OS - The educational operating system
//    Copyright (C) 2015 Edward Nutting
//
//    This program is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 2 of the License, or
//    (at your option) any later version.
//
//    This program is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
//  Project owner: 
//		Email: edwardnutting@outlook.com
//		For paper mail address, please contact via email for details.
//
// ------------------------------------------------------------------------------ //
#endregion
    
//#define SCHEDULER_TRACE
//#define SCHEDULER_HANDLER_TRACE

using System;
using Kernel.FOS_System.Collections;

namespace Kernel.Hardware.Processes
{
    public static unsafe class Scheduler
    {
        private static PriorityQueue ActiveQueue = new PriorityQueue(1024);
        private static PriorityQueue InactiveQueue = new PriorityQueue(1024);
        private static List SuspendedList = new List(1024);

        public enum Priority : int 
        {
            Low = 15,
            Normal = 10,
            High = 5
        }

        public static bool Enabled = false;

        public const int MSFreq = 5;
        private static int UpdatePeriod = 15;
        private static int UpdateCountdown;

        public static void InitProcess(Process process, Priority priority)
        {
            process.Priority = priority;

            bool reenable = Scheduler.Enabled;
            if (reenable)
            {
                Scheduler.Disable();
            }

            for (int i = 0; i < process.Threads.Count; i++)
            {
                Thread t = (Thread)process.Threads[i];

                InitThread(process, t);
            }

            process.Registered = true;

            if (reenable)
            {
                Scheduler.Enable();
            }
        }
        public static void InitThread(Process process, Thread t)
        {
            t.TimeToRunReload = (int)process.Priority;
            t.TimeToRun = t.TimeToRunReload;

            UpdateList(t, false);
        }

        [Drivers.Compiler.Attributes.NoDebug]
        public static void Init()
        {
            //ExceptionMethods.ThePageFaultHandler = HandlePageFault;

            //Disable interrupts - critical section
#if SCHEDULER_TRACE
            BasicConsole.WriteLine(" > Disabling interrupts...");
#endif
            Disable();

            ActiveQueue.Name = "Active Queue";
            InactiveQueue.Name = "Inactive Queue";

            //Load first process and first thread
#if SCHEDULER_TRACE
            BasicConsole.WriteLine(" > Setting current process...");
#endif
            ProcessManager.CurrentProcess = (Process)ProcessManager.Processes[0];
#if SCHEDULER_TRACE
            BasicConsole.WriteLine(" > Setting current thread...");
#endif
            ProcessManager.CurrentThread = (Thread)ProcessManager.CurrentProcess.Threads[0];
#if SCHEDULER_TRACE
            BasicConsole.WriteLine(" > Setting current thread state...");
#endif
            ProcessManager.CurrentThread_State = ProcessManager.CurrentThread.State;
                        
            //Init TSS
#if SCHEDULER_TRACE
            BasicConsole.WriteLine(" > Getting TSS pointer...");
#endif
            TSS* tss = GetTSSPointer();
#if SCHEDULER_TRACE
            BasicConsole.WriteLine(" > Setting esp0...");
#endif
            tss->esp0 = (uint)ProcessManager.CurrentThread_State->KernelStackTop;
#if SCHEDULER_TRACE
            BasicConsole.WriteLine(" > Setting ss0...");
#endif
            //Note: KM CS is loaded from IDT entry on ring 3 -> 0 switch
            tss->ss0 = 0x10; //Kernel mode stack segment = KM Data segment (same for all processes)
            
#if SCHEDULER_TRACE
            BasicConsole.WriteLine(" > Setting cr3...");
#endif
            tss->cr3 = VirtMem.x86.GetCR3();

            //Load Task Register
#if SCHEDULER_TRACE
            BasicConsole.WriteLine(" > Loading TR...");
#endif
            LoadTR();

            //Enable timer
#if SCHEDULER_TRACE
            BasicConsole.WriteLine(" > Adding timer handler...");
#endif
            /*Multiply by 1000000 to get from ms to ns*/
            Hardware.Devices.Timer.Default.RegisterHandler(OnTimerInterrupt, /* MSFreq * 1000000 */ 5000000, true, null);

#if SCHEDULER_TRACE
            BasicConsole.WriteLine(" > Enabling process switching...");
#endif
            Interrupts.Interrupts.EnableProcessSwitching = true;

//#if SCHEDULER_TRACE
            BasicConsole.WriteLine(" > Enabling scheduler...");
//#endif
            UpdateCountdown = UpdatePeriod;

            Enable();
        }
        [Drivers.Compiler.Attributes.PluggedMethod(ASMFilePath = @"ASM\Processes\Scheduler")]
        private static void LoadTR()
        {
        }
        [Drivers.Compiler.Attributes.PluggedMethod(ASMFilePath = null)]
        private static TSS* GetTSSPointer()
        {
            return null;
        }

        [Drivers.Compiler.Attributes.NoDebug]
        [Drivers.Compiler.Attributes.NoGC]
        public static void HandlePageFault(uint eip, uint errorCode, uint address)
        {
            Hardware.VirtMem.MemoryLayout memLayout = ProcessManager.CurrentProcess.TheMemoryLayout;
            BasicConsole.WriteLine("Code pages:");
            string TempDisplayString = "0x        ";
            for (int i = 0; i < memLayout.CodePages.Keys.Count; i++)
            {
                uint vAddr = memLayout.CodePages.Keys[i];
                WriteNumber(TempDisplayString, vAddr);
                BasicConsole.WriteLine(TempDisplayString);
            }
            BasicConsole.WriteLine("Data pages:");
            for (int i = 0; i < memLayout.DataPages.Keys.Count; i++)
            {
                uint vAddr = memLayout.DataPages.Keys[i];
                WriteNumber(TempDisplayString, vAddr);
                BasicConsole.WriteLine(TempDisplayString);
            }
            BasicConsole.DelayOutput(100);
        }
        [Drivers.Compiler.Attributes.NoDebug]
        [Drivers.Compiler.Attributes.NoGC]
        private static void WriteNumber(FOS_System.String str, uint val)
        {
            int offset = 9;
            #region Address
            while (offset > 1)
            {
                uint rem = val & 0xFu;
                switch (rem)
                {
                    case 0:
                        str[offset] = '0';
                        break;
                    case 1:
                        str[offset] = '1';
                        break;
                    case 2:
                        str[offset] = '2';
                        break;
                    case 3:
                        str[offset] = '3';
                        break;
                    case 4:
                        str[offset] = '4';
                        break;
                    case 5:
                        str[offset] = '5';
                        break;
                    case 6:
                        str[offset] = '6';
                        break;
                    case 7:
                        str[offset] = '7';
                        break;
                    case 8:
                        str[offset] = '8';
                        break;
                    case 9:
                        str[offset] = '9';
                        break;
                    case 10:
                        str[offset] = 'A';
                        break;
                    case 11:
                        str[offset] = 'B';
                        break;
                    case 12:
                        str[offset] = 'C';
                        break;
                    case 13:
                        str[offset] = 'D';
                        break;
                    case 14:
                        str[offset] = 'E';
                        break;
                    case 15:
                        str[offset] = 'F';
                        break;
                }
                val >>= 4;
                offset--;
            }

            #endregion
        }

#if SCHEDULER_TRACE
        static bool wasum = false;
        public static bool print = false;
#endif
        [Drivers.Compiler.Attributes.NoDebug]
        [Drivers.Compiler.Attributes.NoGC]
        private static void OnTimerInterrupt(FOS_System.Object state)
        {
            if (!Enabled)
            {
                return;
            }
            
            UpdateCountdown -= MSFreq;

            if (UpdateCountdown <= 0)
            {
                UpdateCountdown = UpdatePeriod;
                UpdateCurrentState();
            }
        }
        [Drivers.Compiler.Attributes.NoDebug]
        [Drivers.Compiler.Attributes.NoGC]
        public static void UpdateCurrentState()
        {
#if SCHEDULER_HANDLER_TRACE
            BasicConsole.SetTextColour(BasicConsole.warning_colour);

            if (Processes.ProcessManager.Processes.Count > 1)
                BasicConsole.WriteLine("Scheduler interrupt started...");
#endif

            if (ProcessManager.CurrentProcess == null ||
               ProcessManager.CurrentThread == null ||
               ProcessManager.CurrentThread_State == null)
            {
                return;
            }

            UpdateInactiveThreads();

            UpdateActiveThreads();

            while (ActiveQueue.Count == 0)
            {
                //#if SCHEDULER_HANDLER_TRACE
                BasicConsole.WriteLine("WARNING: Scheduler preventing infinite loop by early-updating sleeping threads.");
                //#endif
                UpdateInactiveThreads();
            }

            Thread nextThread = (Thread)ActiveQueue.PeekMin();
            ProcessManager.SwitchProcess(nextThread.Owner.Id, (int)nextThread.Id);

            if (!ProcessManager.CurrentThread_State->Started)
            {
                SetupThreadForStart();
            }

#if SCHEDULER_HANDLER_TRACE
            if (Processes.ProcessManager.Processes.Count > 1)
                BasicConsole.WriteLine("Scheduler interrupt ended.");

            BasicConsole.SetTextColour(BasicConsole.default_colour);
#endif
        }
        //[Drivers.Compiler.Attributes.NoDebug]
        //[Drivers.Compiler.Attributes.NoGC]
        //private static void NextProcess(ref int threadIdx, ref int processIdx)
        //{
        //    processIdx++;

        //    if (processIdx >= ProcessManager.Processes.Count)
        //    {
        //        //Go back to start of list
        //        processIdx = 0;
        //    }

        //    threadIdx = NextThread(ProcessManager.THREAD_DONT_CARE, processIdx);
        //}
        //[Drivers.Compiler.Attributes.NoDebug]
        //[Drivers.Compiler.Attributes.NoGC]
        //private static int NextThread(int threadIdx, int processIdx)
        //{
        //    threadIdx++;

        //    Process cProcess = ((Process)ProcessManager.Processes[processIdx]);

        //    while (threadIdx < cProcess.Threads.Count &&
        //          (((Thread)cProcess.Threads[threadIdx]).State->Terminated ||
        //           ((Thread)cProcess.Threads[threadIdx]).TimeToSleep != 0 ||
        //           ((Thread)cProcess.Threads[threadIdx]).Debug_Suspend))
        //    {
        //        threadIdx++;
        //    }
        //    return threadIdx;
        //}
        [Drivers.Compiler.Attributes.NoDebug]
        [Drivers.Compiler.Attributes.NoGC]
        private static void UpdateActiveThreads()
        {
#if SCHEDULER_HANDLER_TRACE
            BasicConsole.WriteLine("Scheduler > Updating active threads...");
            FOS_System.String NumberOfActiveThreadsStr = "Scheduler > Number of active threads: 0x        ";
            ExceptionMethods.FillString((uint)ActiveQueue.Count, 47, NumberOfActiveThreadsStr);
            BasicConsole.WriteLine(NumberOfActiveThreadsStr);

            BasicConsole.WriteLine("Scheduler > Peeking min active thread...");
#endif
            Thread minThread = (Thread)ActiveQueue.PeekMin();
            if (minThread != null &&
                minThread.Key == 0)
            {
#if SCHEDULER_HANDLER_TRACE
                BasicConsole.WriteLine("Scheduler > Extracting min active thread...");
#endif
                ActiveQueue.ExtractMin();
#if SCHEDULER_HANDLER_TRACE
                BasicConsole.WriteLine("Scheduler > Setting time to run of min active thread...");
#endif
                minThread.TimeToRun = minThread.TimeToRunReload;
#if SCHEDULER_HANDLER_TRACE
                BasicConsole.WriteLine("Scheduler > Re-inserting active thread...");
#endif
                ActiveQueue.Insert(minThread);
            }
            
#if SCHEDULER_HANDLER_TRACE
            BasicConsole.WriteLine("Scheduler > Decreasing keys of all active threads...");
#endif
            ActiveQueue.DecreaseAllKeys(1, 0);
            
#if SCHEDULER_HANDLER_TRACE
            BasicConsole.WriteLine("Scheduler > Updated active threads.");
#endif
        }
        [Drivers.Compiler.Attributes.NoDebug]
        [Drivers.Compiler.Attributes.NoGC]
        private static void UpdateInactiveThreads()
        {
#if SCHEDULER_HANDLER_TRACE
            BasicConsole.WriteLine("Scheduler > Updating inactive threads...");
            FOS_System.String NumberOfInactiveThreadsStr = "Scheduler > Number of inactive threads: 0x        ";
            ExceptionMethods.FillString((uint)InactiveQueue.Count, 49, NumberOfInactiveThreadsStr);
            BasicConsole.WriteLine(NumberOfInactiveThreadsStr);

            BasicConsole.WriteLine("Scheduler > Decreasing keys of all inactive threads...");
#endif
            InactiveQueue.DecreaseAllKeys(UpdatePeriod, 0);
            
#if SCHEDULER_HANDLER_TRACE
            BasicConsole.WriteLine("Scheduler > Peeking min inactive thread...");
#endif
            Thread cThread = (Thread)InactiveQueue.PeekMin();
            while (cThread != null && cThread.ActiveState != Thread.ActiveStates.Inactive)
            {
#if SCHEDULER_HANDLER_TRACE
                BasicConsole.WriteLine("Scheduler > Extracting min inactive thread...");
#endif
                InactiveQueue.ExtractMin();
                
#if SCHEDULER_HANDLER_TRACE
                BasicConsole.WriteLine("Scheduler > Updating min inactive thread's list...");
#endif
                UpdateList(cThread, true);
                
#if SCHEDULER_HANDLER_TRACE
                BasicConsole.WriteLine("Scheduler > Moving to next min inactive thread...");
#endif
                cThread = (Thread)InactiveQueue.PeekMin();
            }
            
#if SCHEDULER_HANDLER_TRACE
            BasicConsole.WriteLine("Scheduler > Updated inactive threads.");
#endif
        }
        [Drivers.Compiler.Attributes.NoDebug]
        private static void SetupThreadForStart()
        {
#if SCHEDULER_TRACE
            if (print)
            {
                BasicConsole.WriteLine("Marking thread as started...");
            }
#endif
            ProcessManager.CurrentThread_State->Started = true;
            ProcessManager.CurrentThread_State->Terminated = false;

#if SCHEDULER_TRACE
            if (print)
            {
                BasicConsole.WriteLine("Initialising thread stack...");
            }
#endif
            // Selectors for user-mode must be or'ed with 3 to set privilege level

            uint* stackPtr = (uint*)ProcessManager.CurrentThread_State->ThreadStackTop;
            // Process terminate CS selector
            //      Process terminate should always be jumping into kernel-mode code (for now)
            *stackPtr-- = 8u;
            // Process terminate return pointer
            *stackPtr-- = (uint)Utilities.ObjectUtilities.GetHandle((TerminateMethod)ThreadTerminated);
            if (ProcessManager.CurrentProcess.UserMode)
            {
#if SCHEDULER_TRACE
                if (print)
                {
                    BasicConsole.WriteLine("Setting up UM start stack...");
                }
#endif

                uint WantedStackPtr = (uint)stackPtr;
                *stackPtr-- = 0x23;    //64 - SS after switch to UM
                *stackPtr-- = WantedStackPtr; // 60 - ESP after switch to UM
                *stackPtr-- = 0x0202u; // - 56 - Reserved=1, Interrupt Enable=1, IOPL=0
                *stackPtr-- = 0x1Bu;   // CS - 52
                *stackPtr-- = ProcessManager.CurrentThread_State->StartEIP; // - 48

                *stackPtr-- = 0;    //eax - 44
                *stackPtr-- = 0;    //ecx - 40
                *stackPtr-- = 0;    //edx - 36
                *stackPtr-- = 0;    //ebx - 32
                *stackPtr-- = 0xDEADBEEF;    //esp - 28 - This is actually ignored by Popad instruction
                *stackPtr-- = WantedStackPtr; //ebp - 24
                *stackPtr-- = 0;    //esi - 20
                *stackPtr-- = 0;    //edi - 16

                *stackPtr-- = 0x23u; //ds - 12
                *stackPtr-- = 0x23u; //es - 8
                *stackPtr-- = 0x23u; //fs - 4
                *stackPtr = 0x23u; //gs - 0
            }
            else
            {
#if SCHEDULER_TRACE
                if (print)
                {
                    BasicConsole.WriteLine("Setting up KM start stack...");
                }
#endif

                *stackPtr-- = 0x0202u; // - IOPL=0
                *stackPtr-- = 0x08u;   // CS
                *stackPtr-- = ProcessManager.CurrentThread_State->StartEIP;

                *stackPtr-- = 0;    //eax
                *stackPtr-- = 0;    //ecx
                *stackPtr-- = 0;    //edx
                *stackPtr-- = 0;    //ebx
                *stackPtr-- = 0xDEADBEEF;    //esp - This is actually ignored by Popad instruction
                *stackPtr-- = (uint)ProcessManager.CurrentThread_State->ThreadStackTop;    //ebp
                *stackPtr-- = 0;    //esi
                *stackPtr-- = 0;    //edi 

                *stackPtr-- = 0x10u; //ds
                *stackPtr-- = 0x10u; //es
                *stackPtr-- = 0x10u; //fs
                *stackPtr = 0x10u; //gs
            }

#if SCHEDULER_TRACE
            if (print)
            {
                BasicConsole.WriteLine("Updating thread stack...");
            }
#endif
            ProcessManager.CurrentThread_State->ESP = (uint)stackPtr;

#if SCHEDULER_TRACE
            if (print)
            {
                if (ProcessManager.CurrentProcess.UserMode)
                {
                    BasicConsole.WriteLine("Starting UM thread...");
                }
            }
#endif
        }

        private delegate void TerminateMethod();
        private static void ThreadTerminated()
        {
//#if SCHEDULER_TRACE
            // START - Trace code
            
            BasicConsole.WriteLine("Thread terminated.");
            BasicConsole.WriteLine("Process Name: " + ProcessManager.CurrentProcess.Name + ", Thread Id: " + ProcessManager.CurrentThread.Id);

            //if(reenable)
            //{
            //    Enable();
            //}

            //for (int i = 0; i < 3; i++)
            //{
            //    BasicConsole.Write(".");
            //    Thread.Sleep(1000);
            //}
            // END - Trace code

            //if(reenable)
            //{
            //    Disable();
            //}
//#endif

            // Mark thread as terminated. Leave it to the scheduler to stop running
            //  and the process manager can destroy it later.
            ProcessManager.CurrentThread.LastActiveState = ProcessManager.CurrentThread.ActiveState;
            ProcessManager.CurrentThread_State->Terminated = true;
            UpdateList(ProcessManager.CurrentThread);
            
            //Wait for the scheduler to interrupt us. We will never return here again (inside this thread)
            //  since it has now been terminated.
            while (true)
            {
//#if SCHEDULER_TRACE
                BasicConsole.WriteLine("Still running!");
//#endif
            }
        }

        [Drivers.Compiler.Attributes.NoDebug]
        public static void Enable()
        {
            //BasicConsole.WriteLine("Enabling scheduler...");
            //BasicConsole.DelayOutput(1);
            Enabled = true;
            //Hardware.Interrupts.Interrupts.EnableInterrupts();
        }
        [Drivers.Compiler.Attributes.NoDebug]
        public static void Disable()
        {
            //Hardware.Interrupts.Interrupts.DisableInterrupts();
            Enabled = false;
            //BasicConsole.WriteLine("Disabled scheduler.");
            //BasicConsole.DelayOutput(1);
        }

        public static void UpdateList(Thread t)
        {
            UpdateList(t, false);
        }
        private static void UpdateList(Thread t, bool skipRemove)
        {
            bool reenable = Scheduler.Enabled;
            if (reenable)
            {
                Scheduler.Disable();
            }

            if (!skipRemove)
            {
                switch (t.LastActiveState)
                {
                    case Thread.ActiveStates.NotStarted:
                    case Thread.ActiveStates.Terminated:
                        break;
                    case Thread.ActiveStates.Active:
#if SCHEDULER_HANDLER_TRACE
                        BasicConsole.WriteLine("Scheduler > Deleting thread from Active queue...");
#endif
                        ActiveQueue.Delete(t);
                        break;
                    case Thread.ActiveStates.Inactive:
                        InactiveQueue.Delete(t);
                        break;
                    case Thread.ActiveStates.Suspended:
                        SuspendedList.Remove(t);
                        break;
                }
            }

            switch (t.ActiveState)
            {
                case Thread.ActiveStates.NotStarted:
                    ActiveQueue.Insert(t);
                    break;
                case Thread.ActiveStates.Terminated:
                    break;
                case Thread.ActiveStates.Active:
                    ActiveQueue.Insert(t);
                    break;
                case Thread.ActiveStates.Inactive:
                    InactiveQueue.Insert(t);
                    break;
                case Thread.ActiveStates.Suspended:
                    SuspendedList.Add(t);
                    break;
            }

            if (reenable)
            {
                Scheduler.Enable();
            }
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    public struct TSS
    {
        /* For obvious reasons, do not reorder the fields. */

        public ushort backlink, __blh;
        public uint esp0;
        public ushort ss0, __ss0h;
        public uint esp1;
        public ushort ss1, __ss1h;
        public uint esp2;
        public ushort ss2, __ss2h;
        public uint cr3;
        public uint eip;
        public uint eflags;
        public uint eax, ecx, edx, ebx;
        public uint esp, ebp, esi, edi;
        public ushort es, __esh;
        public ushort cs, __csh;
        public ushort ss, __ssh;
        public ushort ds, __dsh;
        public ushort fs, __fsh;
        public ushort gs, __gsh;
        public ushort ldt, __ldth;
        public ushort trace, bitmap;
    }
}
