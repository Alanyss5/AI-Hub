---
名称：微服务模式
描述：设计具有服务边界、事件驱动通信和弹性模式的微服务架构。在构建分布式系统、分解整体系统或实现微服务时使用。
---

# 微服务模式

掌握微服务架构模式，包括服务边界、服务间通信、数据管理和用于构建分布式系统的弹性模式。

## 何时使用此技能

- 将单体分解为微服务
- 设计服务边界和契约
- 实现服务间通信
- 管理分布式数据和事务
- 构建弹性分布式系统
- 实现服务发现和负载均衡
- 设计事件驱动的架构

## 核心概念

### 1. 服务分解策略

**按业务能力**

- 围绕业务职能组织服务
- 每个服务都有自己的域
- 示例：OrderService、PaymentService、InventoryService

**按子域 (DDD)**

- 核心域，支持子域
- 有界上下文映射到服务
- 明确的所有权和责任

**绞杀无花果图案**

- 逐渐从巨石中提取
- 作为微服务的新功能
- 到旧/新系统的代理路由

### 2. 沟通模式

**同步（请求/响应）**

- REST API
-gRPC
- GraphQL

**异步（事件/消息）**

- 事件流（卡夫卡）
- 消息队列（RabbitMQ、SQS）
- 发布/订阅模式

### 3.数据管理

**每个服务的数据库**

- 每个服务都拥有自己的数据
- 没有共享数据库
- 松耦合

**传奇模式**

- 分布式交易
- 补偿行动
- 最终一致性

### 4.弹性模式

**断路器**

- 重复错误时快速失败
- 防止级联故障

**使用退避重试**

- 瞬态故障处理
- 指数退避

**舱壁**

- 隔离资源
- 限制故障的影响

## 服务分解模式

### 模式 1：按业务能力

````蟒蛇
# 电子商务示例

# 订单服务
订单服务类：
    """处理订单生命周期。"""

    async def create_order(self, order_data: dict) -> 订单：
        订单 = Order.create(order_data)

        # 为其他服务发布事件
        等待 self.event_bus.publish(
            订单创建事件(
                order_id=订单.id,
                customer_id=订单.customer_id,
                项目=订单.项目，
                总计=订单.总计
            ）
        ）

        退货订单

# 支付服务（单独服务）
类支付服务：
    """处理付款处理。"""

    async def process_ payment(self, payment_request: PaymentRequest) -> PaymentResult:
        # 处理付款
        结果 = 等待 self. payment_gateway.charge(
            金额=付款请求.金额，
            客户= payment_request.customer_id
        ）

        如果结果.成功：
            等待 self.event_bus.publish(
                付款完成事件(
                    order_id=付款请求.order_id,
                    transaction_id=结果.transaction_id
                ）
            ）

        返回结果

# 库存服务（单独服务）
库存服务类：
    """处理库存管理。"""

    async def Reserve_items(self, order_id: str, items: List[OrderItem]) -> ReservationResult:
        # 检查可用性
        对于项目中的项目：
            可用 = 等待 self.inventory_repo.get_available(item.product_id)
            如果可用 < item.quantity:
                返回预约结果(
                    成功=假，
                    error=f“{item.product_id} 库存不足”
                ）

        # 保留物品
        预订 = 等待 self.create_reservation(order_id, items)

        等待 self.event_bus.publish(
            库存预留事件(
                订单id=订单id,
                servation_id=reservation.id
            ）
        ）

        返回 ReservationResult(成功=True, 预订=预订)
````

### 模式 2：API 网关

````蟒蛇
从 fastapi 导入 FastAPI、HTTPException、取决于
导入httpx
从断路器输入电路

应用程序 = FastAPI()

API网关类：
    """所有客户端请求的中央入口点。"""

    def __init__(自身):
        self.order_service_url = "http://order-service:8000"
        self. payment_service_url = "http:// payment-service:8001"
        self.inventory_service_url=“http://库存服务:8002”
        self.http_client = httpx.AsyncClient(超时=5.0)

    @电路（failure_threshold = 5，recovery_timeout = 30）
    async def call_order_service(self, 路径: str, 方法: str = "GET", **kwargs):
        """呼叫带有断路器的订购服务。"""
        响应 = 等待 self.http_client.request(
            方法，
            f"{self.order_service_url}{路径}",
            **夸格
        ）
        响应.raise_for_status()
        返回response.json()

    异步 def create_order_aggregate(self, order_id: str) -> dict:
        """聚合来自多个服务的数据。"""
        # 并行请求
        订单、付款、库存 = wait asyncio.gather(
            self.call_order_service(f"/orders/{order_id}"),
            self.call_ payment_service(f"/付款/订单/{order_id}"),
            self.call_inventory_service(f"/预订/订单/{order_id}"),
            return_exceptions=True
        ）

        # 处理部分失败
        结果 = {“订单”：订单}
        如果不是 isinstance(付款，异常)：
            结果[“付款”] = 付款
        如果不是 isinstance(库存，异常)：
            结果[“库存”] = 库存

        返回结果

@app.post("/api/orders")
异步 def create_order(
    order_data：字典，
    网关：APIGateway = Depends()
）：
    """API 网关端点。"""
    尝试：
        # 到订单服务的路由
        order = 等待 gateway.call_order_service(
            “/订单”，
            方法=“POST”，
            json=订单数据
        ）
        返回{“订单”：订单}
    除了 httpx.HTTPError 为 e：
        引发HTTPException（status_code = 503，detail =“订单服务不可用”）
````

## 沟通模式

### 模式 1：同步 REST 通信

````蟒蛇
# 服务A调用服务B
导入httpx
from tenacity import retry, stop_after_attempt, wait_exponential

类服务客户端：
    """具有重试和超时功能的 HTTP 客户端。"""

    def __init__(self, base_url: str):
        self.base_url = base_url
        self.client = httpx.AsyncClient(
            超时=httpx.Timeout(5.0, connect=2.0),
            限制=httpx.Limits(max_keepalive_connections=20)
        ）

    @重试（
        停止=尝试后停止(3),
        等待=wait_exponential（乘数=1，最小值=2，最大值=10）
    ）
    异步 def get(self, 路径: str, **kwargs):
        """自动重试 GET。"""
        响应 = 等待 self.client.get(f"{self.base_url}{path}", **kwargs)
        响应.raise_for_status()
        返回response.json()

    异步 def post(self, 路径: str, **kwargs):
        """POST 请求。"""
        响应 = 等待 self.client.post(f"{self.base_url}{path}", **kwargs)
        响应.raise_for_status()
        返回response.json()

# 用法
payment_client = ServiceClient("http:// payment-service:8001")
结果 = 等待 payment_client.post("/ payment", json= payment_data)
````

### 模式 2：异步事件驱动

````蟒蛇
# 与 Kafka 的事件驱动通信
从 aiokafka 导入 AIOKafkaProducer、AIOKafkaConsumer
导入 json
从数据类导入数据类，asdict
从日期时间导入日期时间

@数据类
类域事件：
    事件 ID：str
    事件类型：str
    聚合 ID：str
    发生时间：日期时间
    数据：字典

事件总线类：
    """事件发布和订阅。"""

    def __init__(self, bootstrap_servers: 列表[str]):
        self.bootstrap_servers = bootstrap_servers
        self.生产者=无

    异步定义开始（自身）：
        self.生产者 = AIOKafkaProducer(
            bootstrap_servers = self.bootstrap_servers，
            value_serializer=lambda v: json.dumps(v).encode()
        ）
        等待 self.生产者.start()

    异步 def 发布（自身，事件：DomainEvent）：
        """将事件发布到 Kafka 主题。"""
        主题=事件.事件类型
        等待 self.生产者.send_and_wait(
            主题，
            值=asdict(事件),
            key=event.aggregate_id.encode()
        ）

    async def subscribe(self, topic: str, handler: callable):
        """订阅事件。"""
        消费者 = AIOKafkaConsumer(
            主题，
            bootstrap_servers = self.bootstrap_servers，
            value_deserializer=lambda v: json.loads(v.decode()),
            group_id="我的服务"
        ）
        等待消费者.start()

        尝试：
            消费者中的消息异步：
                事件数据 = 消息.值
                等待处理程序（event_data）
        最后：等待消费者.stop()

# 订单服务发布事件
异步 def create_order(order_data: dict):
    订单=等待保存订单（订单数据）

    事件 = 域事件(
        event_id=str(uuid.uuid4()),
        event_type="订单已创建",
        aggregate_id=订单.id,
        发生时间=日期时间.now(),
        数据={
            "order_id": 订单.id,
            "customer_id": 订单.customer_id,
            “总计”：订单.总计
        }
    ）

    等待 event_bus.publish(事件)

# 库存服务监听 OrderCreated
异步 def handle_order_created(event_data: dict):
    """对订单创建做出反应。"""
    order_id = event_data["数据"]["order_id"]
    项目 = event_data["数据"]["项目"]

    # 预留库存
    等待 Reserve_inventory(order_id, items)
````

### 模式 3：Saga 模式（分布式事务）

````蟒蛇
# 用于订单履行的 Saga 编排
从枚举导入枚举
从输入导入列表，可调用

类SagaStep：
    “”“传奇中的一步。”“”

    def __init__(
        自我,
        名称：str，
        动作：可调用，
        补偿：可调用
    ）：
        self.name = 名字
        自我行动 = 行动
        自我补偿=补偿

类 SagaStatus（枚举）：
    待处理=“待处理”
    已完成=“已完成”
    补偿=“补偿”
    失败=“失败”

OrderFulfillmentSaga 类：
    “”“为履行订单而精心策划的传奇故事。”“”

    def __init__(自身):
        self.steps: 列表[SagaStep] = [
            传奇步(
                “创建订单”，
                动作= self.create_order，
                补偿=self.cancel_order
            ),
            传奇步(
                “储备库存”，
                行动= self.reserve_inventory，
                补偿=self.release_inventory
            ),
            传奇步(
                “流程_付款”，
                行动= self.process_ payment ，
                补偿=self.refund_ payment
            ),
            传奇步(
                “确认订单”，
                动作=self.confirm_order，
                补偿=self.cancel_order_confirmation
            ）
        ]

    异步 defexecute(self, order_data: dict) -> SagaResult:
        “”“执行传奇步骤。”“”
        已完成步骤 = []
        上下文 = {“订单数据”：订单数据}

        尝试：
            对于 self.steps 中的步骤：
                # 执行步骤
                结果 = 等待步骤.action(上下文)
                如果没有结果.成功：
                    # 补偿
                    等待自我补偿（已完成的步骤，上下文）
                    返回 SagaResult(
                        状态=SagaStatus.FAILED，
                        错误=结果.错误
                    ）

                Completed_steps.append(步骤)
                上下文.更新(结果.数据)

            返回SagaResult（状态= SagaStatus.COMPLETED，数据=上下文）

        除了异常 e：
            # 补偿错误
            等待自我补偿（已完成的步骤，上下文）
            返回 SagaResult(状态=SagaStatus.FAILED, 错误=str(e))

    async def compens(self,completed_steps:List[SagaStep],context:dict):
        """以相反顺序执行补偿动作。"""
        对于反向步骤（completed_steps）：
            尝试：
                等待步骤.补偿（上下文）
            除了异常 e：
                # 记录补偿失败
                print(f"{step.name}: {e} 补偿失败")

    # 步骤实现
    async def create_order(self, context: dict) -> StepResult:
        order = wait order_service.create(context["order_data"])
        return StepResult(success=True, data={"order_id": order.id})

    异步 def cancel_order(self, context: dict):
        等待 order_service.cancel(context["order_id"])

    async def Reserve_inventory(self, context: dict) -> StepResult:
        结果 = 等待 inventory_service.reserve(
            上下文[“order_id”],
            上下文[“订单数据”][“项目”]
        ）
        返回步骤结果（
            成功=结果.成功,
            data={"reservation_id": result.reservation_id}
        ）

    异步 def release_inventory(self, context: dict):
        等待 inventory_service.release(context["reservation_id"])

    异步 def process_ payment(self, context: dict) -> StepResult:
        结果 = 等待 payment_service.charge(
            上下文[“order_id”],
            上下文[“订单数据”][“总计”]）
        返回步骤结果（
            成功=结果.成功,
            data={"transaction_id": result.transaction_id},
            错误=结果.错误
        ）

    异步 def returned_ payment(self, context: dict):
        等待 payment_service.refund(context["transaction_id"])
````

## 弹性模式

### 断路器模式

````蟒蛇
从枚举导入枚举
从日期时间导入日期时间，时间增量
输入 import Callable, Any

类电路状态（枚举）：
    CLOSED = "已关闭" # 正常运行
    OPEN = "open" # 失败，拒绝请求
    HALF_OPEN = "half_open" # 测试是否恢复

断路器类：
    """用于服务呼叫的断路器。"""

    def __init__(
        自我,
        failure_threshold: int = 5,
        恢复超时：int = 30，
        成功阈值：int = 2
    ）：
        self.failure_threshold = failure_threshold
        self.recovery_timeout = recovery_timeout
        self.success_threshold = success_threshold

        self.failure_count = 0
        自我成功计数 = 0
        self.state = CircuitState.CLOSED
        self.opened_at = 无

    async def call(self, func: Callable, *args, **kwargs) -> 任意：
        """使用断路器执行功能。"""

        如果 self.state == CircuitState.OPEN：
            如果 self._should_attempt_reset():
                self.state = CircuitState.HALF_OPEN
            其他：
                raise CircuitBreakerOpenError("断路器打开")

        尝试：
            结果 = 等待 func(*args, **kwargs)
            self._on_success()
            返回结果

        除了异常 e：
            self._on_failure()
            提高

    def _on_success(自我):
        """处理成功调用。"""
        self.failure_count = 0

        如果 self.state == CircuitState.HALF_OPEN：
            self.success_count += 1
            如果 self.success_count >= self.success_threshold：
                self.state = CircuitState.CLOSED
                自我成功计数 = 0

    def _on_failure（自我）：
        """处理失败的呼叫。"""
        self.failure_count += 1

        如果 self.failure_count >= self.failure_threshold：
            self.state = CircuitState.OPEN
            self.opened_at = datetime.now()

        如果 self.state == CircuitState.HALF_OPEN：
            self.state = CircuitState.OPEN
            self.opened_at = datetime.now()

    def _should_attempt_reset(self) -> bool:
        """检查是否有足够的时间重试。"""
        返回（
            datetime.now() - self.opened_at
            > timedelta(秒=self.recovery_timeout)
        ）

# 用法
断路器 = CircuitBreaker(failure_threshold=5, recovery_timeout=30)

异步 def call_ payment_service( payment_data: dict ):
    返回等待断路器.call(
        payment_client.process_ payment,
        付款数据
    ）
````

## 资源

- **references/service-decomposition-guide.md**：分解单体应用
- **references/communication-patterns.md**：同步与异步模式
- **references/saga-implementation.md**：分布式事务
- **assets/Circuit-breaker.py**：生产断路器
- **assets/event-bus-template.py**：Kafka 事件总线实现
- **assets/api-gateway-template.py**：完整的 API 网关

## 最佳实践

1. **服务边界**：与业务能力保持一致
2. **每个服务的数据库**：无共享数据库
3. **API合约**：版本化，向后兼容
4. **尽可能异步**：直接调用的事件
5. **断路器**：在服务失败时快速失败
6. **分布式跟踪**：跨服务跟踪请求
7. **服务注册中心**：动态服务发现
8. **健康检查**：活性和就绪性探测

## 常见陷阱

- **分布式单体**：紧密耦合的服务
- **闲聊服务**：服务间调用过多
- **共享数据库**：通过数据紧密耦合
- **无断路器**：级联故障
- **一切同步**：耦合紧，弹性差
- **不成熟的微服务**：从微服务开始
- **忽略网络故障**：假设网络可靠
- **无补偿逻辑**：无法撤消失败的交易