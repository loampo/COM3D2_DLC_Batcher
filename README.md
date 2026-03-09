# COM3D2 DLC Batcher

## Introduction

This project began out of a personal need to streamline the process of installing DLCs for COM3D2. What started as a simple script has evolved into a more stable and flexible application, designed to make batch-installing DLCs as painless as possible, saving you from the tedious task of handling them one by one.

## How to Use

This tool acts as a **pre-processor** that prepares your DLC files for the official game launcher. It does not install the DLCs directly. Instead, it organizes and processes them so that the game's launcher can recognize and install them correctly.

**Important:** Before using this tool, it is highly recommended to update your game using its official updater. The batcher includes a safety check and may not run if it cannot detect a specific launcher version. While it might work without updating, running the official updater first ensures maximum compatibility.

Follow these steps:

1.  **Select Game Version:** Choose the correct game version from the list at the top. This is crucial for the DLCs to be processed correctly.
2.  **Set SOURCE Folder:** This is the folder where your downloaded DLC archives (`.zip`, `.rar`, `.7z`) or unzipped DLC folders are located.
3.  **Set DESTINATION Folder:** This must be the `dlc` folder inside your main game directory.
4.  **Run the Steps:**
    *   Click **1. Scan** to see a summary of the archives and folders that will be processed.
    *   Click **2. Extract & Copy** to unpack the archives and copy the necessary folders to your destination.
    *   Click **3. Process DLCs** to run the final step, which prepares the DLCs to be recognized by the game.
    *   Alternatively, you can click **Run All** to perform all three steps in sequence.
5.  **Launch the Game:** After the batcher has finished, **start the official game launcher**. The launcher will then detect the prepared files and complete the installation process.

## Notes & Feedback

This application was built to solve a problem I had, and I'm sharing it in the hope that it helps others too.

If you encounter any bugs or have suggestions for improvements, please feel free to open an issue on this GitHub repository. I'm open to feedback and will do my best to address any problems.
