---
名称：传奇编排
描述：为分布式事务和跨聚合工作流程实现传奇模式。在协调多步骤业务流程、处理补偿事务或管理长时间运行的工作流程时使用。
---

# 传奇编排

用于管理分布式事务和长期运行的业务流程的模式。

## 何时使用此技能

- 协调多服务交易
- 实施补偿交易
- 管理长期运行的业务工作流程
- 处理分布式系统中的故障
- 建立订单履行流程
- 实施审批工作流程

## 核心概念

### 1. 传奇类型

````
编排编排
┌──────┐ ┌──────┐ ┌──────┐ ┌────────────┐
│Svc A│─►│Svc B│─►│Svc C│ │ Orchestrator│
└──────┘ └──────┘ └──────┘ └──────┬──────┘
   │ │ │ │
   ▼ ▼ ▼ ┌──────┼──────┐
 活动 活动 活动 ▼ ▼ ▼
                            ┌────┐┌────┐┌────┐
                            │Svc1││Svc2││Svc3│
                            └────┘└────┘└────┘
````

### 2. Saga 执行状态

|状态|描述 |
| ---------------- | ------------------------------------------ |
| **开始** |佐贺发起 |
| **待定** |等待步骤完成 |
| **补偿** |因失败而回滚 |
| **已完成** |所有步骤均成功 |
| **失败** |赔偿后Saga失败|

## 模板

### 模板 1：Saga Orchestrator 基础

````蟒蛇
从 abc 导入 ABC，抽象方法
从数据类导入数据类、字段
从枚举导入枚举
从输入导入列表、字典、任何、可选
从日期时间导入日期时间
导入uuid

类 SagaState(枚举):
    开始=“开始”
    待处理=“待处理”
    补偿=“补偿”
    已完成=“已完成”
    失败=“失败”


@数据类
类SagaStep：
    名称：str
    动作：str
    补偿：str
    状态：str =“待处理”
    结果：可选[Dict] = None
    错误：可选[str] =无
    execute_at: 可选[日期时间] = 无
    compensed_at：可选[日期时间] =无


@数据类
类传奇：
    saga_id：str
    saga_type：str
    州: 佐贺州
    数据：字典[str，任意]
    步骤：列表[SagaStep]
    当前步数：int = 0
    创建时间：日期时间 = 字段（default_factory=datetime.utcnow）
    update_at: 日期时间 = 字段(default_factory=datetime.utcnow)


SagaOrchestrator 类（ABC）：
    """传奇编排器的基类。"""

    def __init__(self, saga_store, event_publisher):
        self.saga_store = saga_store
        self.event_publisher = event_publisher

    @抽象方法
    def Define_steps(self, data: Dict) -> 列表[SagaStep]:
        “”“定义传奇步骤。”“”
        通过

    @属性
    @抽象方法
    def saga_type(self) -> str:
        """唯一的传奇类型标识符。"""
        通过

    async def start(self, data: Dict) -> Saga:
        “”“开始新的传奇。”“”
        传奇=传奇（
            saga_id=str(uuid.uuid4()),
            saga_type=self.saga_type,
            状态=SagaState.STARTED,
            数据=数据，
            步骤= self.define_steps(数据)
        ）
        等待 self.saga_store.save(saga)
        等待 self._execute_next_step(saga)
        回归传奇

    async def handle_step_completed(self, saga_id: str, step_name: str, result: Dict):
        """处理成功的步骤完成。"""
        saga = 等待 self.saga_store.get(saga_id)

        # 更新步骤
        对于 saga.steps 中的步骤：
            如果步骤名称==步骤名称：
                步骤.status = "已完成"
                步骤.结果 = 结果
                步骤.executed_at = datetime.utcnow()
                打破

        saga.current_step += 1
        saga.updated_at = datetime.utcnow()

        # 检查 saga 是否完成
        如果 saga.current_step >= len(saga.steps):
            saga.state = SagaState.COMPLETED
            等待 self.saga_store.save(saga)
            等待 self._on_saga_completed(saga)
        其他：
            saga.state = SagaState.PENDING
            等待 self.saga_store.save(saga)
            等待 self._execute_next_step(saga)

    异步 def handle_step_failed(self, saga_id: str, step_name: str, error: str):
        """处理步骤失败-启动补偿。"""
        saga = 等待 self.saga_store.get(saga_id)

        # 将步骤标记为失败
        对于 saga.steps 中的步骤：如果步骤名称==步骤名称：
                步骤.status = "失败"
                步骤.错误 = 错误
                打破

        saga.state = SagaState.COMPENSATING
        saga.updated_at = datetime.utcnow()
        等待 self.saga_store.save(saga)

        # 从当前步向后开始补偿
        等待 self._compensate(saga)

    异步 def _execute_next_step(self, saga: Saga):
        """执行传奇中的下一步。"""
        如果 saga.current_step >= len(saga.steps):
            返回

        步骤 = saga.steps[saga.current_step]
        步骤.status = "正在执行"
        等待 self.saga_store.save(saga)

        # 发布命令执行步骤
        等待 self.event_publisher.publish(
            步骤.动作,
            {
                “saga_id”：saga.saga_id，
                “step_name”：步骤名称，
                **传奇.数据
            }
        ）

    异步 def _compensate(self, saga: Saga):
        """对已完成的步骤执行补偿。"""
        # 按相反顺序进行补偿
        对于范围内的 i(saga.current_step - 1, -1, -1):
            步骤 = saga.steps[i]
            如果step.status ==“已完成”：
                step.status = "补偿中"
                等待 self.saga_store.save(saga)

                等待 self.event_publisher.publish(
                    阶跃补偿，
                    {
                        “saga_id”：saga.saga_id，
                        “step_name”：步骤名称，
                        “original_result”：步骤结果，
                        **传奇.数据
                    }
                ）

    异步 def handle_compensation_completed(self, saga_id: str, step_name: str):
        """处理补偿完成。"""
        saga = 等待 self.saga_store.get(saga_id)

        对于 saga.steps 中的步骤：
            如果步骤名称==步骤名称：
                step.status = "已补偿"
                step.compensated_at = datetime.utcnow()
                打破

        # 检查所有补偿是否完成
        所有补偿 = 所有（
            s.status in ("已补偿", "待处理", "失败")
            for s in saga.steps 中的 s
        ）

        如果全部补偿：
            saga.state = SagaState.FAILED
            等待 self._on_saga_failed(saga)

        等待 self.saga_store.save(saga)

    异步 def _on_saga_completed(self, saga: Saga):
        """saga 成功完成时调用。"""
        等待 self.event_publisher.publish(
            f"{self.saga_type}已完成",
            {“saga_id”：saga.saga_id，**saga.data}
        ）

    异步 def _on_saga_failed(self, saga: Saga):
        """补偿后 saga 失败时调用。"""
        等待 self.event_publisher.publish(
            f"{self.saga_type}失败",
            {"saga_id": saga.saga_id, "error": "Saga 失败", **saga.data}
        ）
````

### 模板 2：订单履行传奇

````蟒蛇
OrderFulfillmentSaga 类（SagaOrchestrator）：
    """跨服务协调订单履行。"""

    @属性
    def saga_type(self) -> str:
        返回“订单履行”

    def Define_steps(self, data: Dict) -> 列表[SagaStep]:
        返回[
            传奇步(
                名称=“储备库存”，
                行动=“InventoryService.ReserveItems”，
                补偿=“InventoryService.ReleaseReservation”
            ),
            传奇步(
                名称=“流程付款”，
                行动=“PaymentService.ProcessPayment”，
                补偿=“PaymentService.RefundPayment”
            ),
            传奇步(
                名称=“创建发货”，
                行动=“ShippingService.CreateShipment”，
                补偿=“ShippingService.CancelShipment”
            ),
            传奇步(
                名称=“发送确认”，
                操作=“NotificationService.SendOrderConfirmation”，
                补偿=“NotificationService.SendCancellationNotice”
            ）
        ]


# 用法
异步 def create_order(order_data: Dict):
    saga = OrderFulfillmentSaga(saga_store, event_publisher)
    返回等待 saga.start({
        "order_id": order_data["order_id"],
        "customer_id": order_data["customer_id"],
        “项目”：订单数据[“项目”]，
        “付款方法”：订单数据[“付款方法”]，
        “送货地址”：订单数据[“送货地址”]
    })


# 每个服务中的事件处理程序
库存服务类：
    async def handle_reserve_items(self, 命令d：字典）：
        尝试：
            # 预留库存
            保留 = 等待 self.reserve(
                命令[“项目”]，
                命令[“订单 ID”]
            ）
            # 报告成功
            等待 self.event_publisher.publish(
                “SagaStep完成”，
                {
                    “saga_id”：命令[“saga_id”]，
                    "step_name": "储备库存",
                    “结果”：{“reservation_id”：reservation.id}
                }
            ）
        除了 InsufficientInventoryError 为 e：
            等待 self.event_publisher.publish(
                “SagaStep失败”，
                {
                    “saga_id”：命令[“saga_id”]，
                    "step_name": "储备库存",
                    “错误”：str(e)
                }
            ）

    异步 def handle_release_reservation(self, 命令: Dict):
        # 补偿动作
        等待 self.release_reservation(
            命令["original_result"]["reservation_id"]
        ）
        等待 self.event_publisher.publish(
            "佐贺补偿完成",
            {
                “saga_id”：命令[“saga_id”]，
                “步骤名称”：“储备库存”
            }
        ）
````

### 模板 3：基于编排的传奇

````蟒蛇
从数据类导入数据类
输入 import Dict, Any
导入异步

@数据类
类 SagaContext：
    “”“经历了精心设计的传奇事件。”“”
    saga_id：str
    步骤：整数
    数据：字典[str，任意]
    已完成的步骤：列表


OrderChoreographySaga 类：
    """使用事件的基于编排的传奇故事。"""

    def __init__(self, event_bus):
        self.event_bus = event_bus
        self._register_handlers()

    def _register_handlers(自身):
        self.event_bus.subscribe("OrderCreated", self._on_order_created)
        self.event_bus.subscribe("InventoryReserved", self._on_inventory_reserved)
        self.event_bus.subscribe("PaymentProcessed", self._on_ payment_processed)
        self.event_bus.subscribe("ShipmentCreated", self._on_shipment_created)

        # 补偿处理程序
        self.event_bus.subscribe("付款失败", self._on_ payment_failed)
        self.event_bus.subscribe("ShipmentFailed", self._on_shipment_failed)

    异步 def _on_order_created(self, 事件: Dict):
        """第1步：创建订单，预留库存。"""
        等待 self.event_bus.publish("ReserveInventory", {
            “saga_id”：事件[“order_id”]，
            “order_id”：事件[“order_id”]，
            “项目”：事件[“项目”]
        })

    异步 def _on_inventory_reserved(self, event: Dict):
        """第2步：预留库存，处理付款。"""
        等待 self.event_bus.publish("ProcessPayment", {
            “saga_id”：事件[“saga_id”]，
            “order_id”：事件[“order_id”]，
            “金额”：事件[“总金额”]，
            “reservation_id”：事件[“reservation_id”]
        })

    异步 def _on_ payment_processed(self, 事件: Dict):
        """第三步：付款完成，创建发货。"""
        等待 self.event_bus.publish("CreateShipment", {
            “saga_id”：事件[“saga_id”]，
            “order_id”：事件[“order_id”]，
            “ payment_id”：事件[“ payment_id”]
        })

    异步 def _on_shipment_created(self, 事件: Dict):
        """第 4 步：完成 - 发送确认信息。"""
        等待 self.event_bus.publish("订单已完成", {
            “saga_id”：事件[“saga_id”]，
            “order_id”：事件[“order_id”]，
            “tracking_number”：事件[“tracking_number”]
        })

    # 补偿处理程序
    异步 def _on_ payment_failed(self, event: Dict):
        """付款失败-释放库存。"""
        等待 self.event_bus.publish("ReleaseInventory", {
            “saga_id”：事件[“saga_id”]，
            “reservation_id”：事件[“reservation_id”]
        })
        等待 self.event_bus.publish("订单失败", {
            “order_id”：事件[“order_id”]，
            "reason": "支付失败"
        })

    异步 def _on_shipment_failed(self, 事件: Dict):
        """发货失败-退款并释放库存。"""
        等待 self.event_bus.publish("退款付款", {
            “saga_id”：事件[“saga_id”]，
            “ payment_id”：事件[“ payment_id”]
        })
        等待 self.event_bus.publish("ReleaseInventory", {
            “saga_id”：事件[“saga_id”]，
            “reservation_id”：事件[“reservation_id”]
        })
````

### 模板 4：带有超时的传奇````蟒蛇
类 TimeoutSagaOrchestrator(SagaOrchestrator):
    """带有步骤超时的 Saga Orchestrator。"""

    def __init__(self, saga_store, event_publisher, 调度程序):
        super().__init__(saga_store, event_publisher)
        self.scheduler = 调度程序

    异步 def _execute_next_step(self, saga: Saga):
        如果 saga.current_step >= len(saga.steps):
            返回

        步骤 = saga.steps[saga.current_step]
        步骤.status = "正在执行"
        step.timeout_at = datetime.utcnow() + timedelta(分钟=5)
        等待 self.saga_store.save(saga)

        # 安排超时检查
        等待 self.scheduler.schedule(
            f"saga_timeout_{saga.saga_id}_{step.name}",
            self._check_timeout,
            {"saga_id": saga.saga_id, "step_name": 步骤.name},
            run_at=step.timeout_at
        ）

        等待 self.event_publisher.publish(
            步骤.动作,
            {“saga_id”：saga.saga_id，“step_name”：step.name，**saga.data}
        ）

    异步 def _check_timeout(self, data: Dict):
        """检查步骤是否超时。"""
        saga = 等待 self.saga_store.get(data["saga_id"])
        步骤 = next(s for s in saga.steps if s.name == data["step_name"])

        如果step.status ==“正在执行”：
            # 步骤超时 - 失败
            等待 self.handle_step_failed(
                数据[“saga_id”]，
                数据[“步骤名称”]，
                “步骤超时”
            ）
````

## 最佳实践

### 要做的事

- **使步骤幂等** - 可以安全重试
- **仔细设计补偿** - 它们必须有效
- **使用相关 ID** - 用于跨服务跟踪
- **实施超时** - 不要永远等待
- **记录一切** - 用于调试失败

### 不该做的事

- **不要假设立即完成** - 传奇需要时间
- **不要跳过补偿测试** - 最关键的部分
- **不要耦合服务** - 使用异步消息传递
- **不要忽略部分失败** - 优雅地处理

## 资源

- [Saga 模式](https://microservices.io/patterns/data/saga.html)
- [设计数据密集型应用程序](https://dataintense.net/)