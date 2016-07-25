using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Amazon;
using Amazon.AutoScaling;
using Amazon.AutoScaling.Model;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.ElasticLoadBalancing;
using Amazon.ElasticLoadBalancing.Model;
using Amazon.SimpleDB;
using Amazon.SimpleDB.Model;
using Amazon.S3;
using Amazon.S3.Model;

namespace BlueGreenSwitch
{
    class Program
    {
        public static void Main(string[] args)
        {
            //Console.Write(DoBlueGreenSwitch());


            //Console.Write(DoBlueGreenSwitchUsingELB());

            //Console.Write(DoBlueGreenSwitchOnELBThenASG());

            //Console.Write(DoBlueGreenSwitchOnELBWithStandby_Switch());

            //Console.Write(DoBlueGreenSwitchOnELBWithStandby_Commit());

            //Console.Write(DoBlueGreenSwitchOnELBWithStandby_Rollback());



            //Console.Write(BlueGreenImproved.BlueGreenImproved_Switch("LocoProdELB", "LocoTestELB", "Loco-Infra21-ProdServerGroup-XP8NXI3T2SW"));

            //Console.Write(BlueGreenImproved.BlueGreenImproved_Rollback("LocoProdELB", "LocoTestELB", "Loco-Infra21-ProdServerGroup-XP8NXI3T2SW"));

            Console.Write(BlueGreenImproved.BlueGreenImproved_Commit("LocoProdELB", "Loco-Infra21-ProdServerGroup-XP8NXI3T2SW", "LocoNewProdASG", "Loco-Infra21-TestLaunchConfig-7UO4K3AN7G2T"));

            Console.Read();
        }

        public static string DoBlueGreenSwitch()
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

        public static string DoBlueGreenSwitchUsingELB()
        {
            StringBuilder sb = new StringBuilder(1024);
            using (StringWriter sr = new StringWriter(sb))
            {
                IAmazonElasticLoadBalancing aelb = AWSClientFactory.CreateAmazonElasticLoadBalancingClient();
                DeregisterInstancesFromLoadBalancerRequest deregisterInstancesFromLoadBalancerRequest = new DeregisterInstancesFromLoadBalancerRequest();
                RegisterInstancesWithLoadBalancerRequest registerInstancesWithLoadBalancerRequest = new RegisterInstancesWithLoadBalancerRequest();
                try
                {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    // Get prod ELB name
                    var prodELB =
                        aelb.DescribeLoadBalancers()
                            .LoadBalancerDescriptions.Single(elb => elb.LoadBalancerName.Contains("LocoProdELBnoAS"));

                    // Get test ELB name
                    var testELB =
                        aelb.DescribeLoadBalancers()
                            .LoadBalancerDescriptions.Single(elb => elb.LoadBalancerName.Contains("LocoTestELBnoAS"));

                    // Get initial prod instances
                    var initialProdInstances = prodELB.Instances;

                    // Get initial test instances
                    var initialTestInstances = testELB.Instances;

                    // Deregister initial test instances from test ELB
                    Console.WriteLine("Deregister initial test instances from test ELB\n");
                    deregisterInstancesFromLoadBalancerRequest.LoadBalancerName = testELB.LoadBalancerName;
                    deregisterInstancesFromLoadBalancerRequest.Instances = initialTestInstances;

                    var deregisterInstancesFromTestLoadBalancerResponse = aelb.DeregisterInstancesFromLoadBalancer(deregisterInstancesFromLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", testELB.LoadBalancerName);
                    deregisterInstancesFromTestLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

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

                    // Register initial prod instances with test ELB
                    Console.WriteLine("\nRegister initial prod instances with test ELB\n");
                    registerInstancesWithLoadBalancerRequest.LoadBalancerName = testELB.LoadBalancerName;
                    registerInstancesWithLoadBalancerRequest.Instances = initialProdInstances;

                    var registerInstancesWithTestLoadBalancerResponse = aelb.RegisterInstancesWithLoadBalancer(registerInstancesWithLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", testELB.LoadBalancerName);
                    registerInstancesWithTestLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

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

        public static string DoBlueGreenSwitchOnELBThenASG()
        {
            StringBuilder sb = new StringBuilder(1024);
            using (StringWriter sr = new StringWriter(sb))
            {
                IAmazonElasticLoadBalancing aelb = AWSClientFactory.CreateAmazonElasticLoadBalancingClient();
                DeregisterInstancesFromLoadBalancerRequest deregisterInstancesFromLoadBalancerRequest = new DeregisterInstancesFromLoadBalancerRequest();
                RegisterInstancesWithLoadBalancerRequest registerInstancesWithLoadBalancerRequest = new RegisterInstancesWithLoadBalancerRequest();
                try
                {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    // Get prod ELB name
                    var prodELB =
                        aelb.DescribeLoadBalancers()
                            .LoadBalancerDescriptions.Single(elb => elb.LoadBalancerName.Contains("LocoProdELBnoAS"));

                    // Get test ELB name
                    var testELB =
                        aelb.DescribeLoadBalancers()
                            .LoadBalancerDescriptions.Single(elb => elb.LoadBalancerName.Contains("LocoTestELBnoAS"));

                    // Get initial prod instances
                    var initialProdInstances = prodELB.Instances;

                    // Get initial test instances
                    var initialTestInstances = testELB.Instances;

                    // Deregister initial test instances from test ELB
                    Console.WriteLine("Deregister initial test instances from test ELB\n");
                    deregisterInstancesFromLoadBalancerRequest.LoadBalancerName = testELB.LoadBalancerName;
                    deregisterInstancesFromLoadBalancerRequest.Instances = initialTestInstances;

                    var deregisterInstancesFromTestLoadBalancerResponse = aelb.DeregisterInstancesFromLoadBalancer(deregisterInstancesFromLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", testELB.LoadBalancerName);
                    deregisterInstancesFromTestLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

                    // Register initial test instances with prod ELB
                    Console.WriteLine("\nRegister initial test instances with prod ELB\n");
                    registerInstancesWithLoadBalancerRequest.LoadBalancerName = prodELB.LoadBalancerName;
                    registerInstancesWithLoadBalancerRequest.Instances = initialTestInstances;

                    var registerInstancesWithProdLoadBalancerResponse = aelb.RegisterInstancesWithLoadBalancer(registerInstancesWithLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", prodELB.LoadBalancerName);
                    registerInstancesWithProdLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));




                    //// Create a prod AutoScaling group and attach prod ELB to it
                    //Console.WriteLine("\nCreate a prod AutoScaling group and attach prod ELB to it\n");
                    //CreateAutoScalingGroupRequest createAutoScalingGroupRequest = new CreateAutoScalingGroupRequest
                    //{
                    //    AutoScalingGroupName = "ProdServerGroup",
                    //    AvailabilityZones = new List<string>(new String[] { "eu-west-1a", "eu-west-1b" }),
                    //    InstanceId = initialTestInstances.First().InstanceId,
                    //    LoadBalancerNames = new List<string>(new String[] { prodELB.LoadBalancerName }),
                    //    MaxSize = 2,
                    //    MinSize = 0,
                    //    VPCZoneIdentifier = "subnet-876380de,subnet-e6f55483"
                    //};
                    //IAmazonAutoScaling aas = AWSClientFactory.CreateAmazonAutoScalingClient();
                    //var createAutoScalingGroupResponse = aas.CreateAutoScalingGroup(createAutoScalingGroupRequest);

                    //// Attach initial test instances to prod AutoScalingGroup
                    //Console.WriteLine("\nAttach initial test instances to prod AutoScalingGroup (without waiting for completion)\n");
                    //AttachInstancesRequest attachInstancesRequest = new AttachInstancesRequest();
                    //attachInstancesRequest.AutoScalingGroupName = "ProdServerGroup";
                    //attachInstancesRequest.InstanceIds = initialTestInstances.Select(i => i.InstanceId).ToList();

                    //var attachTestInstancesResponse = aas.AttachInstances(attachInstancesRequest);




                    // Deregister initial prod instances from prod ELB
                    Console.WriteLine("\nDeregister initial prod instances from prod ELB\n");
                    deregisterInstancesFromLoadBalancerRequest.LoadBalancerName = prodELB.LoadBalancerName;
                    deregisterInstancesFromLoadBalancerRequest.Instances = initialProdInstances;

                    var deregisterInstancesFromProdLoadBalancerResponse = aelb.DeregisterInstancesFromLoadBalancer(deregisterInstancesFromLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", prodELB.LoadBalancerName);
                    deregisterInstancesFromProdLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

                    // Register initial prod instances with test ELB
                    Console.WriteLine("\nRegister initial prod instances with test ELB\n");
                    registerInstancesWithLoadBalancerRequest.LoadBalancerName = testELB.LoadBalancerName;
                    registerInstancesWithLoadBalancerRequest.Instances = initialProdInstances;

                    var registerInstancesWithTestLoadBalancerResponse = aelb.RegisterInstancesWithLoadBalancer(registerInstancesWithLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", testELB.LoadBalancerName);
                    registerInstancesWithTestLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

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

        public static string DoBlueGreenSwitchOnELBWithStandby_Switch()
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
                            .LoadBalancerDescriptions.Single(elb => elb.LoadBalancerName.Contains("LocoProdELB"));

                    // Get test ELB name
                    var testELB =
                        aelb.DescribeLoadBalancers()
                            .LoadBalancerDescriptions.Single(elb => elb.LoadBalancerName.Contains("LocoTestELB"));

                    // Get initial prod instances
                    var initialProdInstances = prodELB.Instances;

                    // Get initial test instances
                    var initialTestInstances = testELB.Instances;

                    // Deregister initial test instances from test ELB
                    Console.WriteLine("Deregister initial test instances from test ELB\n");
                    deregisterInstancesFromLoadBalancerRequest.LoadBalancerName = testELB.LoadBalancerName;
                    deregisterInstancesFromLoadBalancerRequest.Instances = initialTestInstances;

                    var deregisterInstancesFromTestLoadBalancerResponse = aelb.DeregisterInstancesFromLoadBalancer(deregisterInstancesFromLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", testELB.LoadBalancerName);
                    deregisterInstancesFromTestLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

                    // Put test AutoScalingGroup instances on standby 

                    // Get test AutoScalingGroup name
                    var testASG =
                        aas.DescribeAutoScalingGroups()
                            .AutoScalingGroups.Single(asg => asg.AutoScalingGroupName.Contains("TestServerGroup"));

                    EnterStandbyRequest testEnterStandbyRequest = new EnterStandbyRequest
                    {
                        AutoScalingGroupName = testASG.AutoScalingGroupName,
                        InstanceIds = testASG.Instances.Select(i => i.InstanceId).ToList(),
                        ShouldDecrementDesiredCapacity = true
                    };

                    var testEnterStandbyResponse = aas.EnterStandby(testEnterStandbyRequest);

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

                    // Get prod AutoScalingGroup name
                    var prodASG =
                        aas.DescribeAutoScalingGroups()
                            .AutoScalingGroups.Single(asg => asg.AutoScalingGroupName.Contains("ProdServerGroup"));

                    EnterStandbyRequest prodEnterStandbyRequest = new EnterStandbyRequest
                    {
                        AutoScalingGroupName = prodASG.AutoScalingGroupName,
                        InstanceIds = prodASG.Instances.Select(i => i.InstanceId).ToList(),
                        ShouldDecrementDesiredCapacity = true
                    };

                    var prodEnterStandbyResponse = aas.EnterStandby(prodEnterStandbyRequest);
                   
                    // Register initial prod instances with test ELB
                    Console.WriteLine("\nRegister initial prod instances with test ELB\n");
                    registerInstancesWithLoadBalancerRequest.LoadBalancerName = testELB.LoadBalancerName;
                    registerInstancesWithLoadBalancerRequest.Instances = initialProdInstances;

                    var registerInstancesWithTestLoadBalancerResponse = aelb.RegisterInstancesWithLoadBalancer(registerInstancesWithLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", testELB.LoadBalancerName);
                    registerInstancesWithTestLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

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

        public static string DoBlueGreenSwitchOnELBWithStandby_Rollback()
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
                            .LoadBalancerDescriptions.Single(elb => elb.LoadBalancerName.Contains("LocoProdELB"));

                    // Get test ELB name
                    var testELB =
                        aelb.DescribeLoadBalancers()
                            .LoadBalancerDescriptions.Single(elb => elb.LoadBalancerName.Contains("LocoTestELB"));

                    // Get initial prod instances
                    var initialProdInstances = testELB.Instances;

                    // Get initial test instances
                    var initialTestInstances = prodELB.Instances;

                    // Deregister initial prod instances from test ELB
                    Console.WriteLine("Deregister initial prod instances from test ELB\n");
                    deregisterInstancesFromLoadBalancerRequest.LoadBalancerName = testELB.LoadBalancerName;
                    deregisterInstancesFromLoadBalancerRequest.Instances = initialProdInstances;

                    var deregisterInstancesFromTestLoadBalancerResponse = aelb.DeregisterInstancesFromLoadBalancer(deregisterInstancesFromLoadBalancerRequest);

                    Console.WriteLine("{0} instances now: ", testELB.LoadBalancerName);
                    deregisterInstancesFromTestLoadBalancerResponse.Instances.ForEach(i => Console.WriteLine(i.InstanceId));

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

                    // Get prod AutoScalingGroup name
                    var prodASG =
                        aas.DescribeAutoScalingGroups()
                            .AutoScalingGroups.Single(asg => asg.AutoScalingGroupName.Contains("ProdServerGroup"));

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

                    // Put test AutoScalingGroup instances back in service

                    // Get test AutoScalingGroup name
                    var testASG =
                        aas.DescribeAutoScalingGroups()
                            .AutoScalingGroups.Single(asg => asg.AutoScalingGroupName.Contains("TestServerGroup"));

                    ExitStandbyRequest testExitStandbyRequest = new ExitStandbyRequest
                    {
                        AutoScalingGroupName = testASG.AutoScalingGroupName,
                        InstanceIds = testASG.Instances.Select(i => i.InstanceId).ToList(),
                    };

                    var testEnterStandbyResponse = aas.ExitStandby(testExitStandbyRequest);

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

        public static string DoBlueGreenSwitchOnELBWithStandby_Commit()
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

                    // Get prod ASG LaunchConfiguration (LC) name
                    var initialProdLC = prodASG.LaunchConfigurationName;

                    // Get test ASG LaunchConfiguration (LC) name
                    var initialTestLC = prodASG.LaunchConfigurationName;

                    // Update prod ASG LC to initial test LC
                    UpdateAutoScalingGroupRequest updateProdAutoScalingGroupRequest = new UpdateAutoScalingGroupRequest
                    {
                        AutoScalingGroupName = prodASG.AutoScalingGroupName,
                        LaunchConfigurationName = initialTestLC,
                        MinSize = 2
                        
                    };

                    var updateProdAutoScalingGroupResponse = aas.UpdateAutoScalingGroup(updateProdAutoScalingGroupRequest);

                    // Update test ASG LC to initial test LC
                    UpdateAutoScalingGroupRequest updateTestAutoScalingGroupRequest = new UpdateAutoScalingGroupRequest
                    {
                        AutoScalingGroupName = testASG.AutoScalingGroupName,
                        LaunchConfigurationName = initialProdLC,
                        MinSize = 2

                    };

                    var updateTestAutoScalingGroupResponse = aas.UpdateAutoScalingGroup(updateTestAutoScalingGroupRequest);

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


                    //// Detach initial test instances from test AutoScalingGroup
                    //Console.WriteLine("Detach initial test instances from test AutoScalingGroup\n");
                    //detachInstancesRequest.AutoScalingGroupName = testASG.AutoScalingGroupName;
                    //detachInstancesRequest.InstanceIds = initialTestInstances.Select(i => i.InstanceId).ToList();
                    //detachInstancesRequest.ShouldDecrementDesiredCapacity = true;

                    //var detachTestInstancesResponse = aas.DetachInstances(detachInstancesRequest);

                    //var testASGActivities = detachTestInstancesResponse.Activities;
                    //testASGActivities.ForEach(PrintActivity);

                    //// wait till instances are detached from test AutoScalingGroup
                    //// DetachInstancesAsync would handles this???
                    //while (testASGActivities.Any(a => a.StatusCode == ScalingActivityStatusCode.InProgress))
                    //{
                    //    Thread.Sleep(5000);
                    //    var describeScalingActivitiesRequest = new DescribeScalingActivitiesRequest();
                    //    describeScalingActivitiesRequest.ActivityIds =
                    //        testASGActivities
                    //            .Select(a => a.ActivityId)
                    //            .ToList();
                    //    describeScalingActivitiesRequest.AutoScalingGroupName = testASG.AutoScalingGroupName;
                    //    var response = aas.DescribeScalingActivities(describeScalingActivitiesRequest);
                    //    testASGActivities = response.Activities;
                    //    testASGActivities.ForEach(PrintActivity);
                    //}

                    //// Attach initial test instances to prod AutoScalingGroup
                    //Console.WriteLine("\nAttach initial test instances to prod AutoScalingGroup\n");
                    //attachInstancesRequest.AutoScalingGroupName = prodASG.AutoScalingGroupName;
                    //attachInstancesRequest.InstanceIds = initialTestInstances.Select(i => i.InstanceId).ToList();

                    //var attachTestInstancesResponse = aas.AttachInstances(attachInstancesRequest);

                    //List<Activity> prodASGActivities = new List<Activity>();

                    //// wait for instances to be attached to prod AutoScalingGroup
                    //// AttachInstancesAsync() would handle this???
                    //do
                    //{
                    //    Thread.Sleep(5000);
                    //    var describeScalingActivitiesRequest = new DescribeScalingActivitiesRequest();

                    //    describeScalingActivitiesRequest.ActivityIds =
                    //        prodASGActivities
                    //            .Select(a => a.ActivityId)
                    //            .ToList();
                    //    describeScalingActivitiesRequest.AutoScalingGroupName = prodASG.AutoScalingGroupName;

                    //    var response = aas.DescribeScalingActivities(describeScalingActivitiesRequest);

                    //    prodASGActivities = response.Activities.Where(a => a.StatusCode == ScalingActivityStatusCode.InProgress).ToList();
                    //    prodASGActivities.ForEach(PrintActivity);
                    //} while (prodASGActivities.Count > 0);

                    //// Detach initial prod instances from prod AutoScalingGroup
                    //Console.WriteLine("\nDetach initial prod instances from prod AutoScalingGroup\n");
                    //detachInstancesRequest.AutoScalingGroupName = prodASG.AutoScalingGroupName;
                    //detachInstancesRequest.InstanceIds = initialProdInstances.Select(i => i.InstanceId).ToList();
                    //detachInstancesRequest.ShouldDecrementDesiredCapacity = true;

                    //var detachProdInstancesResponse = aas.DetachInstances(detachInstancesRequest);

                    ////var prodASGActivities = detachProdInstancesResponse.Activities;
                    //prodASGActivities = detachProdInstancesResponse.Activities;
                    //prodASGActivities.ForEach(PrintActivity);

                    //// wait till instances are detached from prod AutoScalingGroup
                    //// DetachInstancesAsync would handles this???
                    //while (prodASGActivities.Any(a => a.StatusCode == ScalingActivityStatusCode.InProgress))
                    //{
                    //    Thread.Sleep(5000);
                    //    var describeScalingActivitiesRequest = new DescribeScalingActivitiesRequest();
                    //    describeScalingActivitiesRequest.ActivityIds =
                    //        prodASGActivities
                    //            .Select(a => a.ActivityId)
                    //            .ToList();
                    //    describeScalingActivitiesRequest.AutoScalingGroupName = prodASG.AutoScalingGroupName;
                    //    var response = aas.DescribeScalingActivities(describeScalingActivitiesRequest);
                    //    prodASGActivities = response.Activities;
                    //    prodASGActivities.ForEach(PrintActivity);
                    //}

                    // Attach initial prod instances to test AutoScalingGroup
                    Console.WriteLine("\nAttach initial prod instances to test AutoScalingGroup (without waiting for completion)\n");
                    attachInstancesRequest.AutoScalingGroupName = testASG.AutoScalingGroupName;
                    attachInstancesRequest.InstanceIds = initialProdInstances.Select(i => i.InstanceId).ToList();

                    var attachProdInstancesResponse = aas.AttachInstances(attachInstancesRequest);
                    

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