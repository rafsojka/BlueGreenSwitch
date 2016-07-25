using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Amazon;
using Amazon.AutoScaling;
using Amazon.AutoScaling.Model;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.ElasticLoadBalancing;
using Amazon.ElasticLoadBalancing.Model;
using Instance = Amazon.ElasticLoadBalancing.Model.Instance;
using Tag = Amazon.AutoScaling.Model.Tag;

namespace BlueGreenSwitch
{
    public static class BlueGreenImproved
    {
        public static string BlueGreenImproved_Switch(string prodELBName, string testELBName, string prodASGName)
        {
            StringBuilder sb = new StringBuilder(1024);
            using (StringWriter sr = new StringWriter(sb))
            {
                IAmazonElasticLoadBalancing aelb = AWSClientFactory.CreateAmazonElasticLoadBalancingClient();
                DeregisterInstancesFromLoadBalancerRequest deregisterInstancesFromLoadBalancerRequest = new DeregisterInstancesFromLoadBalancerRequest();
                RegisterInstancesWithLoadBalancerRequest registerInstancesWithLoadBalancerRequest = new RegisterInstancesWithLoadBalancerRequest();

                IAmazonAutoScaling aas = AWSClientFactory.CreateAmazonAutoScalingClient();
                IAmazonEC2 aec2 = AWSClientFactory.CreateAmazonEC2Client();

                try
                {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    // Get prod ELB name
                    var prodELB =
                        aelb.DescribeLoadBalancers()
                            .LoadBalancerDescriptions.Single(elb => elb.LoadBalancerName == prodELBName);

                    // Get initial prod instances
                    var initialProdInstances = prodELB.Instances;

                    // Get test ELB name
                    var testELB =
                        aelb.DescribeLoadBalancers()
                            .LoadBalancerDescriptions.Single(elb => elb.LoadBalancerName == testELBName);

                    // Get initial test instances
                    var initialTestInstances = testELB.Instances;

                    // Get prod AutoScalingGroup name
                    var prodASG =
                        aas.DescribeAutoScalingGroups()
                            .AutoScalingGroups.Single(asg => asg.AutoScalingGroupName == prodASGName);

                    // Register initial test instances with prod ELB
                    Console.WriteLine("\nRegister initial test instances with prod ELB\n");
                    registerInstancesWithLoadBalancerRequest.LoadBalancerName = prodELB.LoadBalancerName;
                    registerInstancesWithLoadBalancerRequest.Instances = initialTestInstances;

                    var registerInstancesWithProdLoadBalancerResponse = aelb.RegisterInstancesWithLoadBalancer(registerInstancesWithLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", prodELB.LoadBalancerName);
                    registerInstancesWithProdLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

                    // Deregister initial prod instances from prod ELB
                    Console.WriteLine("\nDeregister initial prod instances from prod ELB\n");
                    deregisterInstancesFromLoadBalancerRequest.LoadBalancerName = prodELB.LoadBalancerName;
                    deregisterInstancesFromLoadBalancerRequest.Instances = initialProdInstances;

                    var deregisterInstancesFromProdLoadBalancerResponse = aelb.DeregisterInstancesFromLoadBalancer(deregisterInstancesFromLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", prodELB.LoadBalancerName);
                    deregisterInstancesFromProdLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

                    // Put prod AutoScalingGroup instances on standby
                    Console.WriteLine("\nPut prod AutoScalingGroup instances on standby\n");
                    EnterStandbyRequest prodEnterStandbyRequest = new EnterStandbyRequest
                    {
                        AutoScalingGroupName = prodASG.AutoScalingGroupName,
                        InstanceIds = prodASG.Instances.Select(i => i.InstanceId).ToList(),
                        ShouldDecrementDesiredCapacity = true
                    };

                    var prodEnterStandbyResponse = aas.EnterStandby(prodEnterStandbyRequest);

                    // Deregister initial test instances from test ELB
                    Console.WriteLine("Deregister initial test instances from test ELB\n");
                    deregisterInstancesFromLoadBalancerRequest.LoadBalancerName = testELB.LoadBalancerName;
                    deregisterInstancesFromLoadBalancerRequest.Instances = initialTestInstances;

                    var deregisterInstancesFromTestLoadBalancerResponse = aelb.DeregisterInstancesFromLoadBalancer(deregisterInstancesFromLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", testELB.LoadBalancerName);
                    deregisterInstancesFromTestLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

                    stopwatch.Stop();
                    TimeSpan ts = stopwatch.Elapsed;

                    sr.Write("Blue-Green switch performed successfully.");

                    string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                    ts.Hours, ts.Minutes, ts.Seconds,
                    ts.Milliseconds / 10);
                    Console.WriteLine("\nRunTime " + elapsedTime);
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

        public static string BlueGreenImproved_Rollback(string prodELBName, string testELBName, string prodASGName)
        {
            StringBuilder sb = new StringBuilder(1024);
            using (StringWriter sr = new StringWriter(sb))
            {
                IAmazonElasticLoadBalancing aelb = AWSClientFactory.CreateAmazonElasticLoadBalancingClient();
                DeregisterInstancesFromLoadBalancerRequest deregisterInstancesFromLoadBalancerRequest = new DeregisterInstancesFromLoadBalancerRequest();
                RegisterInstancesWithLoadBalancerRequest registerInstancesWithLoadBalancerRequest = new RegisterInstancesWithLoadBalancerRequest();

                IAmazonAutoScaling aas = AWSClientFactory.CreateAmazonAutoScalingClient();

                try
                {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    // Get prod ELB name
                    var prodELB =
                        aelb.DescribeLoadBalancers()
                            .LoadBalancerDescriptions.Single(elb => elb.LoadBalancerName == prodELBName);

                    // Get test ELB name
                    var testELB =
                        aelb.DescribeLoadBalancers()
                            .LoadBalancerDescriptions.Single(elb => elb.LoadBalancerName == testELBName);

                    // Get prod AutoScalingGroup name
                    var prodASG =
                        aas.DescribeAutoScalingGroups()
                            .AutoScalingGroups.Single(asg => asg.AutoScalingGroupName == prodASGName);

                    // Get initial prod instances
                    var initialProdInstancesIds = new List<string>();
                    prodASG.Instances.ForEach(instance => initialProdInstancesIds.Add(instance.InstanceId));
                    var initialProdInstances = new List<Instance>();
                    initialProdInstancesIds.ForEach(id => initialProdInstances.Add(new Instance{InstanceId = id}));

                    // Get initial test instances
                    var initialTestInstances = prodELB.Instances;

                    // Register initial prod instances with prod ELB
                    Console.WriteLine("\nRegister initial prod instances with prod ELB\n");
                    registerInstancesWithLoadBalancerRequest.LoadBalancerName = prodELB.LoadBalancerName;
                    registerInstancesWithLoadBalancerRequest.Instances = initialProdInstances;

                    var registerInstancesWithProdLoadBalancerResponse = aelb.RegisterInstancesWithLoadBalancer(registerInstancesWithLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", prodELB.LoadBalancerName);
                    registerInstancesWithProdLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

                    // Deregister initial test instances from prod ELB
                    Console.WriteLine("\nDeregister initial test instances from prod ELB\n");
                    deregisterInstancesFromLoadBalancerRequest.LoadBalancerName = prodELB.LoadBalancerName;
                    deregisterInstancesFromLoadBalancerRequest.Instances = initialTestInstances;

                    var deregisterInstancesFromProdLoadBalancerResponse = aelb.DeregisterInstancesFromLoadBalancer(deregisterInstancesFromLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", prodELB.LoadBalancerName);
                    deregisterInstancesFromProdLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

                    // Put prod AutoScalingGroup instances back in service
                    Console.WriteLine("\nPut prod AutoScalingGroup instances back in service\n");
                    ExitStandbyRequest prodExitStandbyRequest = new ExitStandbyRequest
                    {
                        AutoScalingGroupName = prodASG.AutoScalingGroupName,
                        InstanceIds = prodASG.Instances.Select(i => i.InstanceId).ToList()
                    };

                    var prodExitStandbyResponse = aas.ExitStandby(prodExitStandbyRequest);

                    // Register initial test instances with test ELB
                    Console.WriteLine("\nRegister initial test instances with test ELB\n");
                    registerInstancesWithLoadBalancerRequest.LoadBalancerName = testELB.LoadBalancerName;
                    registerInstancesWithLoadBalancerRequest.Instances = initialTestInstances;

                    var registerInstancesWithTestLoadBalancerResponse = aelb.RegisterInstancesWithLoadBalancer(registerInstancesWithLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", testELB.LoadBalancerName);
                    registerInstancesWithTestLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

                    stopwatch.Stop();
                    TimeSpan ts = stopwatch.Elapsed;

                    sr.Write("Blue-Green rollback performed successfully.");

                    string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                    ts.Hours, ts.Minutes, ts.Seconds,
                    ts.Milliseconds / 10);
                    Console.WriteLine("\nRunTime " + elapsedTime);
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

        public static string BlueGreenImproved_Commit(string prodELBName, string oldProdASGName, string newProdASGName, string newProdASGLaunchConfigName)
        {
            StringBuilder sb = new StringBuilder(1024);
            using (StringWriter sr = new StringWriter(sb))
            {
                IAmazonElasticLoadBalancing aelb = AWSClientFactory.CreateAmazonElasticLoadBalancingClient();
                IAmazonAutoScaling aas = AWSClientFactory.CreateAmazonAutoScalingClient();
                
                try
                {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    // Get prod ELB name
                    var prodELB =
                        aelb.DescribeLoadBalancers()
                            .LoadBalancerDescriptions.Single(elb => elb.LoadBalancerName == prodELBName);

                    // Get current prod instances
                    var currentProdInstances = prodELB.Instances;

                    // Get old prod AutoScalingGroup
                    var oldProdASG =
                        aas.DescribeAutoScalingGroups()
                            .AutoScalingGroups.Single(asg => asg.AutoScalingGroupName == oldProdASGName);

                    // Create a new prod ASG with launch configuration corresponding to new instances
                    Console.WriteLine("\nCreate a new prod ASG with launch configuration corresponding to new instances\n");
                    var newProdASGTags = new List<Tag>();
                    foreach (var oldTag in oldProdASG.Tags)
                    {
                        if (oldTag.Key.StartsWith("aws"))
                            continue;

                        newProdASGTags.Add(new Tag
                        {
                            Key = oldTag.Key,
                            Value = oldTag.Value,
                            PropagateAtLaunch = oldTag.PropagateAtLaunch
                        });
                    }

                    CreateAutoScalingGroupRequest createAutoScalingGroupRequest = new CreateAutoScalingGroupRequest
                    {
                        AutoScalingGroupName = newProdASGName,
                        AvailabilityZones = oldProdASG.AvailabilityZones,
                        LaunchConfigurationName = newProdASGLaunchConfigName,
                        //InstanceId = initialTestInstances.First().InstanceId,
                        LoadBalancerNames = oldProdASG.LoadBalancerNames,
                        MaxSize = 2,
                        MinSize = 0,
                        Tags = newProdASGTags,
                        VPCZoneIdentifier = oldProdASG.VPCZoneIdentifier
                    };


                    var createAutoScalingGroupResponse = aas.CreateAutoScalingGroup(createAutoScalingGroupRequest);

                    // Attach current prod instances to the new prod ASG
                    Console.WriteLine("\nAttach current prod instances to the new prod ASG (without waiting for completion)\n");
                    AttachInstancesRequest attachInstancesRequest = new AttachInstancesRequest();
                    attachInstancesRequest.AutoScalingGroupName = newProdASGName;
                    attachInstancesRequest.InstanceIds = currentProdInstances.Select(i => i.InstanceId).ToList();

                    var attachCurrentProdInstancesResponse = aas.AttachInstances(attachInstancesRequest);

                    // Delete baseline instances (still in old prod ASG, in standby)
                    Console.WriteLine("\nDelete baseline instances (still in old prod ASG, in standby)\n");
                    oldProdASG.Instances.ForEach(oldProdASGInstance => aas.TerminateInstanceInAutoScalingGroup( new TerminateInstanceInAutoScalingGroupRequest
                    {
                        InstanceId = oldProdASGInstance.InstanceId,
                        ShouldDecrementDesiredCapacity = false
                    }));

                    // Delete old prod ASG
                    Console.WriteLine("\nDelete old prod ASG\n");
                    aas.DeleteAutoScalingGroup(new DeleteAutoScalingGroupRequest
                    {
                        AutoScalingGroupName = oldProdASGName,
                        ForceDelete = true
                    });

                    stopwatch.Stop();
                    TimeSpan ts = stopwatch.Elapsed;

                    sr.Write("Blue-Green commit performed successfully.");

                    string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                    ts.Hours, ts.Minutes, ts.Seconds,
                    ts.Milliseconds / 10);
                    Console.WriteLine("\nRunTime " + elapsedTime);
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
    }
}
