// // Copyright (c) Microsoft. All rights reserved.
// // Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Windows;
using System.Windows.Controls;

namespace MDriveSync.WPF
{
    public class JobListDataTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item != null && item is Task)
            {
                var taskitem = (Task)item;
                var window = Application.Current.MainWindow;
                if (taskitem.Priority == 1)
                {
                    return window.FindResource("DriveAccountTemplate") as DataTemplate;
                }

                return window.FindResource("DriveJobTemplate") as DataTemplate;
            }

            return null;
        }
    }
}