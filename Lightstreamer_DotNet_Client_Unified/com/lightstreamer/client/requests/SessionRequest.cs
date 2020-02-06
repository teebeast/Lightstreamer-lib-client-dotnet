﻿/*
 * Copyright (c) 2004-2019 Lightstreamer s.r.l., Via Campanini, 6 - 20124 Milano, Italy.
 * All rights reserved.
 * www.lightstreamer.com
 *
 * This software is the confidential and proprietary information of
 * Lightstreamer s.r.l.
 * You shall not disclose such Confidential Information and shall use it
 * only in accordance with the terms of the license agreement you entered
 * into with Lightstreamer s.r.l.
 */
namespace com.lightstreamer.client.requests
{
    public class SessionRequest : LightstreamerRequest
    {
        private bool polling;
        private long delay;

        public SessionRequest(bool polling, long delay)
        {
            this.polling = polling;
            this.delay = delay;
        }

        public virtual bool Polling
        {
            set { }
            get
            {
                return polling;
            }
        }

        public virtual long Delay
        {
            set { }
            get
            {
                return delay;
            }
        }

        public override string RequestName
        {
            set { }
            get
            {
                return null;
            }
        }
    }
}