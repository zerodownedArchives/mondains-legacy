using System;
using System.Collections;
using System.Collections.Generic;

using Server;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.ContextMenus;
using EDI = Server.Mobiles.EscortDestinationInfo;

namespace Server.Engines.Quests
{
	public class BaseEscort : MondainQuester
	{
		private static TimeSpan m_EscortDelay = TimeSpan.FromMinutes( 5.0 );
		private static Dictionary<Mobile,Mobile> m_EscortTable = new Dictionary<Mobile,Mobile>();

		public override bool InitialInnocent{ get{ return true; } }
		public override bool IsInvulnerable{ get{ return false; } }
		public override bool Commandable{ get{ return false; } }
		public override Type[] Quests { get { return null; } }

		private BaseQuest m_Quest;

		public BaseQuest Quest
		{
			get { return m_Quest; }
			set { m_Quest = value; }
		}
		
		private DateTime m_LastSeenEscorter;

		public DateTime LastSeenEscorter
		{
			get{ return m_LastSeenEscorter; }
			set{ m_LastSeenEscorter = value; }
		}

		private Timer m_DeleteTimer;
		private bool m_Checked;
		
		public BaseEscort() : base()
		{
			AI = AIType.AI_Melee;
			FightMode = FightMode.Aggressor;
			RangePerception = 22;
			RangeFight = 1;
			ActiveSpeed = 0.2;
			PassiveSpeed = 1.0;

			ControlSlots = 0;
		}
		
		public BaseEscort( Serial serial ) : base( serial )
		{
		}	
		
		public override void OnTalk( PlayerMobile player )
		{
			if ( AcceptEscorter( player ) )
				base.OnTalk( player );
		}

		public override bool CanBeRenamedBy( Mobile from )
		{
			return ( from.AccessLevel >= AccessLevel.GameMaster );
		}

		public override void AddCustomContextEntries( Mobile from, List<ContextMenuEntry> list )
		{
			if ( from.Alive && from == ControlMaster )
				list.Add( new AbandonEscortEntry( this ) );

			base.AddCustomContextEntries( from, list );
		}

		public override void OnAfterDelete()
		{
			if ( m_Quest != null )
			{
				m_Quest.RemoveQuest();

				if ( m_Quest.Owner != null )
					m_EscortTable.Remove( m_Quest.Owner );
			}
			
			base.OnAfterDelete();
		}

		public override void OnThink()
		{
			base.OnThink();
			
			CheckAtDestination();
		}

		public override void InitBody()
		{
			SetStr( 90, 100 );
			SetDex( 90, 100 );
			SetInt( 15, 25 );

			Hue = Utility.RandomSkinHue();
			Female = Utility.RandomBool();
			Name = NameList.RandomName( Female ? "female" : "male" );
			Race = Race.Human;

			Utility.AssignRandomHair( this );
			Utility.AssignRandomFacialHair( this );
		}

		public override void Serialize( GenericWriter writer )
		{
			base.Serialize( writer );

			writer.Write( (int) 0 ); // version

			writer.Write( m_DeleteTimer != null );

			if ( m_DeleteTimer != null )
				writer.WriteDeltaTime( m_DeleteTimer.Next );
		}

		public override void Deserialize( GenericReader reader )
		{
			base.Deserialize( reader );

			int version = reader.ReadInt();

			if ( reader.ReadBool() )
			{
				DateTime deleteTime = reader.ReadDeltaTime();
				m_DeleteTimer = Timer.DelayCall( deleteTime - DateTime.Now, new TimerCallback( Delete ) );
			}
		}

		public void AddHash( PlayerMobile player )
		{
			m_EscortTable[ player ] = this;
		}

		public virtual void StartFollow()
		{
			StartFollow( ControlMaster );
		}
		
		public virtual void StartFollow( Mobile escorter )
		{		
			ActiveSpeed = 0.1;
			PassiveSpeed = 0.2;

			ControlOrder = OrderType.Follow;
			ControlTarget = escorter;

			CurrentSpeed = 0.1;
		}
		
		public virtual void StopFollow()
		{
			ActiveSpeed = 0.2;
			PassiveSpeed = 1.0;

			ControlOrder = OrderType.None;
			ControlTarget = null;

			CurrentSpeed = 1.0;

			SetControlMaster( null );
		}

		public virtual void BeginDelete( Mobile m )
		{
			StopFollow();
			
			if ( m != null )
				m_EscortTable.Remove( m );
			
			m_DeleteTimer = Timer.DelayCall( TimeSpan.FromSeconds( 45.0 ), new TimerCallback( Delete ) );
		}

		public virtual bool AcceptEscorter( Mobile m )
		{
			if ( !m.Alive )
			{
				return false;
			}
			else if ( m_DeleteTimer != null )
			{
				Say( 500898 ); // I am sorry, but I do not wish to go anywhere.
				return false;
			}
			else if ( Controlled )
			{
				if ( m == ControlMaster )
					m.SendGump( new MondainQuestGump( m_Quest, MondainQuestGump.Section.InProgress, false ) );
				else
					Say( 500897 ); // I am already being led!
				
				return false;
			}
			else if ( !m.InRange( Location, 5 ) )
			{
				Say( 500348 ); // I am too far away to do that.
				return false;
			}
			else if ( m_EscortTable.ContainsKey( m ) )
			{
				Say( 500896 ); // I see you already have an escort.
				return false;
			}
			else if ( m is PlayerMobile && ( ( (PlayerMobile) m ).LastEscortTime + m_EscortDelay ) >= DateTime.Now )
			{
				int minutes = (int) Math.Ceiling( ( ( ( (PlayerMobile) m ).LastEscortTime + m_EscortDelay ) - DateTime.Now ).TotalMinutes );

				Say( "You must rest {0} minute{1} before we set out on this journey.", minutes, minutes == 1 ? "" : "s" );
				return false;
			}

			return true;
		}

		public virtual EscortObjective GetObjective()
		{
			if ( m_Quest != null )
			{
				for ( int i = 0; i < m_Quest.Objectives.Count; i++ )
				{
					EscortObjective escort = m_Quest.Objectives[ i ] as EscortObjective;

					if ( escort != null && !escort.Completed && !escort.Failed )
						return escort;
				}
			}

			return null;
		}

		public virtual Mobile GetEscorter()
		{
			Mobile master = ControlMaster;

			if ( master == null || !Controlled )
			{
				return master;
			}
			else if ( master.Map != Map || !master.InRange( Location, 30 ) || !master.Alive )
			{
				TimeSpan lastSeenDelay = DateTime.Now - m_LastSeenEscorter;

				if ( lastSeenDelay >= TimeSpan.FromMinutes( 2.0 ) )
				{
					EscortObjective escort = GetObjective();

					if ( escort != null )
					{
						master.SendLocalizedMessage( 1071194 ); // You have failed your escort quest�
						master.PlaySound( 0x5B3 );
						escort.Fail();
					}

					master.SendLocalizedMessage( 1042473 ); // You have lost the person you were escorting.
					Say( 1005653 ); // Hmmm.  I seem to have lost my master.

					StopFollow();
					m_EscortTable.Remove( master );
					m_DeleteTimer = Timer.DelayCall( TimeSpan.FromSeconds( 5.0 ), new TimerCallback( Delete ) );

					return null;
				}
				else
				{
					ControlOrder = OrderType.Stay;
				}
			}
			else
			{
				if ( ControlOrder != OrderType.Follow )
					StartFollow( master );

				m_LastSeenEscorter = DateTime.Now;
			}

			return master;
		}

		public virtual Region GetDestination()
		{
			return null;
		}
		
		public virtual bool CheckAtDestination()
		{
			if ( m_Quest != null )
			{
				EscortObjective escort = GetObjective();

				if ( escort == null )
					return false;

				Mobile escorter = GetEscorter();

				if ( escorter == null )
					return false;

				if ( escort.Region != null && escort.Region.Contains( Location ) )
				{
					Say( 1042809, escorter.Name ); // We have arrived! I thank thee, ~1_PLAYER_NAME~! I have no further need of thy services. Here is thy pay.

					escort.Complete();

					if ( m_Quest.Completed )
					{
						escorter.SendLocalizedMessage( 1046258, null, 0x23 ); // Your quest is complete.		

						if ( QuestHelper.AnyRewards( m_Quest ) )
							escorter.SendGump( new MondainQuestGump( m_Quest, MondainQuestGump.Section.Rewards, false, true ) );
						else
							m_Quest.GiveRewards();

						escorter.PlaySound( m_Quest.CompleteSound );

						StopFollow();
						m_EscortTable.Remove( escorter );
						m_DeleteTimer = Timer.DelayCall( TimeSpan.FromSeconds( 5.0 ), new TimerCallback( Delete ) );

						// fame
						Misc.Titles.AwardFame( escorter, escort.Fame, true );

						// compassion
						bool gainedPath = false;

						PlayerMobile pm = escorter as PlayerMobile;

						if ( pm != null )
						{
							if ( pm.CompassionGains > 0 && DateTime.Now > pm.NextCompassionDay )
							{
								pm.NextCompassionDay = DateTime.MinValue;
								pm.CompassionGains = 0;
							}

							if ( pm.CompassionGains >= 5 ) // have already gained 5 times in one day, can gain no more
							{
								pm.SendLocalizedMessage( 1053004 ); // You must wait about a day before you can gain in compassion again.
							}
							else if ( VirtueHelper.Award( pm, VirtueName.Compassion, escort.Compassion, ref gainedPath ) )
							{
								pm.SendLocalizedMessage( 1074949, null, 0x2A );  // You have demonstrated your compassion!  Your kind actions have been noted.

								if ( gainedPath )
									pm.SendLocalizedMessage( 1053005 ); // You have achieved a path in compassion!
								else
									pm.SendLocalizedMessage( 1053002 ); // You have gained in compassion.

								pm.NextCompassionDay = DateTime.Now + TimeSpan.FromDays( 1.0 ); // in one day CompassionGains gets reset to 0
								++pm.CompassionGains;
							}
							else
							{
								pm.SendLocalizedMessage( 1053003 ); // You have achieved the highest path of compassion and can no longer gain any further.
							}
						}
					}
					else
						escorter.PlaySound( m_Quest.UpdateSound );

					return true;
				}
			}
			else if ( !m_Checked )
			{
				Region region = GetDestination();

				if ( region != null && region.Contains( Location ) )
				{
					m_DeleteTimer = Timer.DelayCall( TimeSpan.FromSeconds( 5.0 ), new TimerCallback( Delete ) );
					m_Checked = true;
				}
			}		

			return false;
		}
		
		private class AbandonEscortEntry : ContextMenuEntry
		{
			private BaseEscort m_Mobile;
	
			public AbandonEscortEntry( BaseEscort m ) : base( 6102, 3 )
			{
				m_Mobile = m;
			}
	
			public override void OnClick()
			{
				Owner.From.SendLocalizedMessage( 1071194 ); // You have failed your escort quest�
				Owner.From.PlaySound( 0x5B3 );
				m_Mobile.Delete();
			}
		}
	}
}