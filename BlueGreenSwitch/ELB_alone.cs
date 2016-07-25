using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon;
using Amazon.AutoScaling;
using Amazon.ElasticLoadBalancing;
using Amazon.ElasticLoadBalancing.Model;

namespace BlueGreenSwitch
{
    class ELB_alone
    {
        public static string ELB_alone_blue_green_switch()
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
    }
}
