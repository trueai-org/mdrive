// // Copyright (c) Microsoft. All rights reserved.
// // Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.ObjectModel;

namespace MDriveSync.WPF
{
    public class Tasks : ObservableCollection<Task>
    {
        public Tasks()
        {
            Add(new Task("Groceries", "Pick up Groceries and Detergent", 2, JobStateColor.Success));
            Add(new Task("Laundry", "Do my Laundry", 2, JobStateColor.Success));
            Add(new Task("Email", "Email clients", 1, JobStateColor.Fail));
            Add(new Task("Clean", "Clean my office", 3, JobStateColor.Fail));
            Add(new Task("Dinner", "Get ready for family reunion", 1, JobStateColor.Success));
            Add(new Task("Proposals", "Review new budget proposals", 2, JobStateColor.Fail));
        }
    }
}