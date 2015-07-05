﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HardwareATA = Kernel.Hardware.ATA;

namespace Kernel.Hardware.Testing
{
    public partial class ATATests : Test
    {
        public void Test_LongRead(OutputMessageDel OutputMessage, OutputWarningDel OutputWarning, OutputErrorDel OutputError)
        {
            OutputMessage("ATATests : Test_LongRead", "Test started.");

            // The device we are going to test
            ATA.PATA TestDevice = null;

            // Search for PATA device
            OutputMessage("ATATests : Test_LongRead", "Searching for PATA device...");
            for (int i = 0; i < DeviceManager.Devices.Count; i++)
            {
                Device ADevice = (Device)DeviceManager.Devices[i];
                if (ADevice is ATA.PATA)
                {
                    TestDevice = (ATA.PATA)ADevice;
                    break;
                }
            }

            // Check that we found a device
            if (TestDevice == null)
            {
                OutputWarning("ATATests : Test_LongRead", "No PATA device found. Aborting test.");
                return;
            }
            else
            {
                OutputMessage("ATATests : Test_LongRead", ((FOS_System.String)"Device found. Controller ID: ") + 
                        (TestDevice.ControllerID == HardwareATA.ATA.ControllerID.Primary ? "Primary" : "Secondary") + " , Position: " + 
                        (TestDevice.BusPosition == HardwareATA.ATA.BusPosition.Master ? "Master" : "Slave"));
            }

            // Create a buffer for storing up to 16 blocks of data
            OutputMessage("ATATests : Test_LongRead", "Creating data buffer...");
            byte[] buffer = new byte[32 * (int)(uint)TestDevice.BlockSize];
            OutputMessage("ATATests : Test_LongRead", "done.");

            try
            {
                OutputMessage("ATATests : Test_LongRead", "Calculating statistical data...");
                ulong FractionOfDisk = FOS_System.Math.Divide(TestDevice.BlockCount, 10);
                ulong PercentileOfFraction = FOS_System.Math.Divide(FractionOfDisk, 100);
                ulong dist = 0;
                bool a = true;
                OutputMessage("ATATests : Test_LongRead", "done.");

                OutputMessage("ATATests : Test_LongRead", "Reading disk 32 sectors at a time...");
                // Attempt to read every sector of the disk 32 at a time
                for (ulong i = 0; i < FractionOfDisk; i += 32, dist += 32)
                {
                    TestDevice.ReadBlock(i, 32, buffer);

                    if (dist >= PercentileOfFraction)
                    {
                        dist -= PercentileOfFraction;
                        if (a)
                        {
                            OutputMessage("ATATests : Test_LongRead", "[+1% complete] (a)");
                        }
                        else
                        {
                            OutputMessage("ATATests : Test_LongRead", "[+1% complete] (b)");
                        }

                        a = !a;
                    }
                }
                OutputMessage("ATATests : Test_LongRead", "done.");
            }
            catch
            {
                OutputError("ATATests : Test_LongRead", ExceptionMethods.CurrentException.Message);
            }


            OutputMessage("ATATests : Test_LongRead", "Test complete.");
        }
    }
}
