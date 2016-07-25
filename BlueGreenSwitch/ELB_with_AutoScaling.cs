using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.AutoScaling;
using Amazon.AutoScaling.Model;

namespace BlueGreenSwitch
{
    class ELB_with_AutoScaling
    {
        public static string ELB_with_AutoScaling_blue_green_switchh()
        {
            StringBuilder sb = new StringBuilder(1024);
            using (StringWriter sr = new StringWriter(sb))
            {
                IAmazonAutoScaling aas = AWSClientFactory.CreateAmazonAutoScalingClient();
                DetachInstancesRequest detachInstancesRequest = new DetachInstancesRequest();
                AttachInstancesRequest attachInstancesRequest = new AttachInstancesRequest();
                try
                {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    // Get prod AutoScalingGroup name
                    var prodASG =
                        aas.DescribeAutoScalingGroups()
                            .AutoScalingGroups.Single(asg => asg.AutoScalingGroupName.Contains("ProdServerGroup"));

                    // Get test AutoScalingGroup name
                    var testASG =
                        aas.DescribeAutoScalingGroups()
                            .AutoScalingGroups.Single(asg => asg.AutoScalingGroupName.Contains("TestServerGroup"));

                    // Get initial prod instances
                    var initialProdInstances = prodASG.Instances;

                    // Get initial test instances
                    var initialTestInstances = testASG.Instances;

                    // Detach initial test instances from test AutoScalingGroup
                    Console.WriteLine("Detach initial test instances from test AutoScalingGroup\n");
                    detachInstancesRequest.AutoScalingGroupName = testASG.AutoScalingGroupName;
                    detachInstancesRequest.InstanceIds = initialTestInstances.Select(i => i.InstanceId).ToList();
                    detachInstancesRequest.ShouldDecrementDesiredCapacity = true;

                    var detachTestInstancesResponse = aas.DetachInstances(detachInstancesRequest);

                    var testASGActivities = detachTestInstancesResponse.Activities;
                    testASGActivities.ForEach(PrintActivity);

                    // wait till instances are detached from test AutoScalingGroup
                    // DetachInstancesAsync would handles this???
                    while (testASGActivities.Any(a => a.StatusCode == ScalingActivityStatusCode.InProgress))
                    {
                        Thread.Sleep(5000);
                        var describeScalingActivitiesRequest = new DescribeScalingActivitiesRequest();
                        describeScalingActivitiesRequest.ActivityIds =
                            testASGActivities
                                .Select(a => a.ActivityId)
                                .ToList();
                        describeScalingActivitiesRequest.AutoScalingGroupName = testASG.AutoScalingGroupName;
                        var response = aas.DescribeScalingActivities(describeScalingActivitiesRequest);
                        testASGActivities = response.Activities;
                        testASGActivities.ForEach(PrintActivity);
                    }

                    // Attach initial test instances to prod AutoScalingGroup
                    Console.WriteLine("\nAttach initial test instances to prod AutoScalingGroup\n");
                    attachInstancesRequest.AutoScalingGroupName = prodASG.AutoScalingGroupName;
                    attachInstancesRequest.InstanceIds = initialTestInstances.Select(i => i.InstanceId).ToList();

                    var attachTestInstancesResponse = aas.AttachInstances(attachInstancesRequest);

                    List<Activity> prodASGActivities = new List<Activity>();

                    // wait for instances to be attached to prod AutoScalingGroup
                    // AttachInstancesAsync() would handle this???
                    do
                    {
                        Thread.Sleep(5000);
                        var describeScalingActivitiesRequest = new DescribeScalingActivitiesRequest();

                        describeScalingActivitiesRequest.ActivityIds =
                            prodASGActivities
                                .Select(a => a.ActivityId)
                                .ToList();
                        describeScalingActivitiesRequest.AutoScalingGroupName = prodASG.AutoScalingGroupName;

                        var response = aas.DescribeScalingActivities(describeScalingActivitiesRequest);

                        prodASGActivities = response.Activities.Where(a => a.StatusCode == ScalingActivityStatusCode.InProgress).ToList();
                        prodASGActivities.ForEach(PrintActivity);
                    } while (prodASGActivities.Count > 0);

                    // Detach initial prod instances from prod AutoScalingGroup
                    Console.WriteLine("\nDetach initial prod instances from prod AutoScalingGroup\n");
                    detachInstancesRequest.AutoScalingGroupName = prodASG.AutoScalingGroupName;
                    detachInstancesRequest.InstanceIds = initialProdInstances.Select(i => i.InstanceId).ToList();
                    detachInstancesRequest.ShouldDecrementDesiredCapacity = true;

                    var detachProdInstancesResponse = aas.DetachInstances(detachInstancesRequest);

                    //var prodASGActivities = detachProdInstancesResponse.Activities;
                    prodASGActivities = detachProdInstancesResponse.Activities;
                    prodASGActivities.ForEach(PrintActivity);

                    // wait till instances are detached from prod AutoScalingGroup
                    // DetachInstancesAsync would handles this???
                    while (prodASGActivities.Any(a => a.StatusCode == ScalingActivityStatusCode.InProgress))
                    {
                        Thread.Sleep(5000);
                        var describeScalingActivitiesRequest = new DescribeScalingActivitiesRequest();
                        describeScalingActivitiesRequest.ActivityIds =
                            prodASGActivities
                                .Select(a => a.ActivityId)
                                .ToList();
                        describeScalingActivitiesRequest.AutoScalingGroupName = prodASG.AutoScalingGroupName;
                        var response = aas.DescribeScalingActivities(describeScalingActivitiesRequest);
                        prodASGActivities = response.Activities;
                        prodASGActivities.ForEach(PrintActivity);
                    }

                    // Attach initial prod instances to test AutoScalingGroup
                    Console.WriteLine("\nAttach initial prod instances to test AutoScalingGroup (without waiting for completion)\n");
                    attachInstancesRequest.AutoScalingGroupName = testASG.AutoScalingGroupName;
                    attachInstancesRequest.InstanceIds = initialProdInstances.Select(i => i.InstanceId).ToList();

                    var attachProdInstancesResponse = aas.AttachInstances(attachInstancesRequest);

                    stopwatch.Stop();
                    TimeSpan ts = stopwatch.Elapsed;


                    sr.Write("Blue-Green switch performed successfully.");

                    string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                    ts.Hours, ts.Minutes, ts.Seconds,
                    ts.Milliseconds / 10);
                    Console.WriteLine("RunTime " + elapsedTime);
                }
                catch (AmazonAutoScalingException ex)
                {
                    if (ex.ErrorCode != null && ex.ErrorCode.Equals("AuthFailure"))
                    {
                        sr.WriteLine("The account you are using is not signed up for Amazon EC2.");
                        sr.WriteLine("You can sign up for Amazon EC2 at http://aws.amazon.com/ec2");
                    }
                    else
                    {
                        sr.WriteLine("Caught Exception: " + ex.Message);
                        sr.WriteLine("Response Status Code: " + ex.StatusCode);
                        sr.WriteLine("Error Code: " + ex.ErrorCode);
                        sr.WriteLine("Error Type: " + ex.ErrorType);
                        sr.WriteLine("Request ID: " + ex.RequestId);
                    }
                }


                return sb.ToString();
            }
        }

        private static void PrintActivity(Activity activity)
        {
            Console.WriteLine("ActivityId\t\t: {0}", activity.ActivityId);
            Console.WriteLine("AutoScalingGroupName\t: {0}", activity.AutoScalingGroupName);
            Console.WriteLine("Cause\t\t\t: {0}", activity.Cause);
            Console.WriteLine("Description\t\t: {0}", activity.Description);
            Console.WriteLine("Details\t\t\t: {0}", activity.Details);
            Console.WriteLine("EndTime\t\t\t: {0}", activity.EndTime);
            Console.WriteLine("Progress\t\t: {0}", activity.Progress);
            Console.WriteLine("StartTime\t\t: {0}", activity.StartTime);
            Console.WriteLine("StatusCode\t\t: {0}", activity.StatusCode);
            Console.WriteLine("StatusMessage\t\t: {0}\n", activity.StatusMessage);
        }
    }
}
